using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.Voxels;
using Sandbox.Game.Entities;

using Priority_Queue;

// MyVoxelConstants.VOXEL_SIZE_IN_METRES

namespace LLE
{
	public enum NodeType { Unknown, Free, Mixed, Blocked, Top }

	/// <summary>
	/// Octree node. Can be a large free zone (super-cell) or a small block.
	/// </summary>
	public class OctreeNode
	{
		public Vector3I Min;
		public int Size;
		public NodeType Type;
		
		public HashSet<OctreeNode> Neighbors;
		public OctreeNode[] Children;

		public Vector3D Center => new Vector3D(Min.X + Size / 2.0, Min.Y + Size / 2.0, Min.Z + Size / 2.0);
	}

	class FPQNode : FastPriorityQueueNode
	{
		public OctreeNode Node;
	}

	/// <summary>
	/// Cell source for OctreeAStar: voxel map or cube grid.
	/// Cell coordinates are lod-0, starting at (0,0,0).
	/// </summary>
	public interface ICellSpace
	{
		Vector3I Size { get; }        // in cells (lod 0)
		int CoarseLod { get; }        // nodes with lod >= CoarseLod are always subdivided (Top)
		double CellSize { get; }      // meters per cell
		bool IsValid { get; }
		NodeType CellType(Vector3I min, Vector3I max, int lod);
		MatrixD LocalToWorld { get; } // cell space -> world (includes CellSize scale); queried per use (grids move)
	}

	public class VoxelCellSpace : ICellSpace
	{
		private readonly MyVoxelBase _voxel;

		private static readonly MyStorageData storage = new MyStorageData();

		public VoxelCellSpace(MyVoxelBase voxel)
		{
			_voxel = voxel;
		}

		public Vector3I Size => _voxel.Storage.Size;
		public int CoarseLod => 4; // 3 = 8m
		public double CellSize => 1.0;
		public bool IsValid => _voxel != null && !_voxel.MarkedForClose;
		public MatrixD LocalToWorld => MatrixD.CreateTranslation(_voxel.PositionLeftBottomCorner);

		public NodeType CellType(Vector3I min, Vector3I max, int lod)
		{
			return VoxelCellType(_voxel, min, max, lod - 1);
		}

		public static NodeType VoxelCellType(MyVoxelBase voxel, Vector3I min, Vector3I max, int lod = 0)
		{
			Vector3I storageMax = voxel.Storage.Size - 1;
			Vector3I coordMin = min >> lod;
			Vector3I coordMax = max >> lod;

			storage.Resize(coordMin, coordMax);
			voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.Material, lod, coordMin, coordMax);

			Vector3I offset = Vector3I.Zero;

			var index = storage.ComputeLinear(ref offset);
			byte v0 = storage.Material(index);

			Vector3I p;
			for (p.X = coordMin.X; p.X <= coordMax.X; p.X++)
			{
				for (p.Y = coordMin.Y; p.Y <= coordMax.Y; p.Y++)
				{
					for (p.Z = coordMin.Z; p.Z <= coordMax.Z; p.Z++)
					{
						offset = p - coordMin;
						index = storage.ComputeLinear(ref offset);

						var v = storage.Material(index);
						if(v != v0)
							return NodeType.Mixed;
					}
				}
			}

			if (v0 == byte.MaxValue) return NodeType.Free;
			return NodeType.Blocked;
		}
	}

	public class GridCellSpace : ICellSpace
	{
		private readonly IMyCubeGrid _grid;

		// Offset and Size are captured at construction; block add/remove or grid
		// resize invalidates this space (rebuild it). Invalidation is future work.
		private readonly Vector3I _offset; // cell (0,0,0) = grid cell (grid.Min - Border)
		private readonly Vector3I _size;

		private const int Border = 2;

		public GridCellSpace(IMyCubeGrid grid)
		{
			_grid = grid;
			_offset = grid.Min - Border;
			_size = grid.Max - grid.Min + 1 + 2 * Border;
		}

		public Vector3I Size => _size;
		public int CoarseLod => 3; // grids have no LOD, CellType scans cells: max 4x4x4 per node
		public double CellSize => _grid.GridSize;
		public bool IsValid => _grid != null && !_grid.MarkedForClose;

		public MatrixD LocalToWorld
		{
			get
			{	// corner of cell (0,0,0) lies at grid-local (_offset - 0.5) * GridSize
				double s = _grid.GridSize;
				return MatrixD.CreateScale(s)
					* MatrixD.CreateTranslation((new Vector3D(_offset) - 0.5) * s)
					* _grid.WorldMatrix;
			}
		}

		public NodeType CellType(Vector3I min, Vector3I max, int lod)
		{
			var gridMin = min + _offset;
			var gridMax = max + _offset;

			// Entirely outside grid bounds - no blocks there
			if (gridMin.X > _grid.Max.X || gridMax.X < _grid.Min.X ||
				gridMin.Y > _grid.Max.Y || gridMax.Y < _grid.Min.Y ||
				gridMin.Z > _grid.Max.Z || gridMax.Z < _grid.Min.Z)
				return NodeType.Free;

			bool anyBlocked = false;
			bool anyFree = false;

			Vector3I p;
			for (p.X = gridMin.X; p.X <= gridMax.X; p.X++)
			{
				for (p.Y = gridMin.Y; p.Y <= gridMax.Y; p.Y++)
				{
					for (p.Z = gridMin.Z; p.Z <= gridMax.Z; p.Z++)
					{
						if (_grid.GetCubeBlock(p) != null) anyBlocked = true;
						else anyFree = true;
					}
				}
			}

			if (!anyBlocked) return NodeType.Free;
			if (!anyFree) return NodeType.Blocked;
			return NodeType.Mixed;
		}
	}

	public class OctreeAStar
	{
		private readonly ICellSpace _space;
		private readonly OctreeNode _root;

		public struct Statistic_
		{	public int Unknown, Top, Free, Mixed, Blocked;

			public override string ToString() => $"Unknown={Unknown} Top={Top} Free={Free} Mixed={Mixed} Blocked={Blocked}";
		}

		public Statistic_ Statistic;

		public OctreeAStar(ICellSpace space)
		{
			_space = space;

			Vector3I size = space.Size;
			var maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));

			// In-game voxels are power-of-two sized (Debug.Assert(source.Size3D.IsPowerOfTwo));
			// grids are not, so round the root up.
			int rootSize = 1;
			while (rootSize < maxDimension) rootSize <<= 1;

			_root = new OctreeNode { Min = Vector3I.Zero, Size = rootSize, Type = NodeType.Unknown };
			++Statistic.Unknown;
		}

		private int CalculateLodLevel(int size)
		{	int lod = 0;
			while (size > 1) { size >>= 1; ++lod; }
			return lod;
		}

		public OctreeNode GetNodeAt(Vector3I pos)
		{
			if (!_space.IsValid) return null;

			var result = new List<OctreeNode>();
			CollectInRange(_root, pos, pos, result, false);

			return result.Count > 0 ? result[0] : null;
		}

		private void CollectInRange(OctreeNode node, Vector3I queryMin, Vector3I queryMax,
			List<OctreeNode> result, bool freeOnly)
		{
			var nodeMax = node.Min + node.Size - 1;

			if (node.Min.X > queryMax.X || nodeMax.X < queryMin.X ||
				node.Min.Y > queryMax.Y || nodeMax.Y < queryMin.Y ||
				node.Min.Z > queryMax.Z || nodeMax.Z < queryMin.Z)
				return;

			if (node.Type == NodeType.Unknown)
			{
				--Statistic.Unknown;
				int lod = CalculateLodLevel(node.Size);

				if (lod >= _space.CoarseLod)
					node.Type = NodeType.Top;
				else
				{
					var cellMin = node.Min;
					var cellMax = node.Min + node.Size - 1;
					node.Type = _space.CellType(cellMin, cellMax, lod);
					//Utilities.Log($"CellType {node.Min} / {cellMin}-{cellMax} / {lod} / {node.Type}");
				}

				switch(node.Type)
				{
					case NodeType.Top: ++Statistic.Top; break;
					case NodeType.Free: ++Statistic.Free; break;
					case NodeType.Mixed: ++Statistic.Mixed; break;
					case NodeType.Blocked: ++Statistic.Blocked; break;
				}
			}

			if (node.Type != NodeType.Mixed && node.Type != NodeType.Top)
			{
				if(!freeOnly || node.Type == NodeType.Free) result.Add(node);
				return;
			}

			if (node.Children == null)
				node.Children = new OctreeNode[8];

			var half = node.Size / 2;
			for (int i = 0; i < 8; i++)
			{
				int ix = (i & 1) != 0 ? half : 0;
				int iy = (i & 2) != 0 ? half : 0;
				int iz = (i & 4) != 0 ? half : 0;

				var childMin = node.Min + new Vector3I(ix, iy, iz);
				var childMax = childMin + half - 1;

				// Skip allocation if child is outside query range
				if (childMin.X > queryMax.X || childMax.X < queryMin.X ||
					childMin.Y > queryMax.Y || childMax.Y < queryMin.Y ||
					childMin.Z > queryMax.Z || childMax.Z < queryMin.Z)
					continue;

				if (node.Children[i] == null)
				{
					node.Children[i] = new OctreeNode { Min = childMin, Size = half, Type = NodeType.Unknown };
					++Statistic.Unknown;
				}

				CollectInRange(node.Children[i], queryMin, queryMax, result, freeOnly);
			}
		}

		private void ComputeNeighbors(OctreeNode node)
		{
			var neighbors = new List<OctreeNode>();
			var queryMin = node.Min - Vector3I.One;
			var queryMax = node.Min + node.Size;
			CollectInRange(_root, queryMin, queryMax, neighbors, true);

			var set = new HashSet<OctreeNode>();
			var nodeMax = node.Min + node.Size - 1;
			foreach (var n in neighbors)
			{
				if (n == node) continue;
				var nMax = n.Min + n.Size - 1;

				// Keep only 6-connected (face-adjacent) neighbors.
				// Nodes are face-adjacent when they touch along exactly one axis
				// and their projections on the other two axes overlap.
				int touchingAxes = 0;
				if (nodeMax.X + 1 == n.Min.X || nMax.X + 1 == node.Min.X) touchingAxes++;
				if (nodeMax.Y + 1 == n.Min.Y || nMax.Y + 1 == node.Min.Y) touchingAxes++;
				if (nodeMax.Z + 1 == n.Min.Z || nMax.Z + 1 == node.Min.Z) touchingAxes++;

				if (touchingAxes == 1)
					set.Add(n);
			}
			node.Neighbors = set;
		}

		/// <summary>
		/// Converts an octree node (cell indices) to a world-space box.
		/// Returned as center + local box because grids are rotated (OBB, not AABB).
		/// </summary>
		public void NodeToWorldBox(OctreeNode node, out MatrixD matrix, out BoundingBoxD localBox)
		{
			var lw = _space.LocalToWorld;
			double s = _space.CellSize;

			// lw rotation part carries uniform CellSize scale - strip it, localBox is in meters
			matrix = lw;
			matrix.Right = lw.Right / s;
			matrix.Up = lw.Up / s;
			matrix.Forward = lw.Forward / s;
			matrix.Translation = Vector3D.Transform(node.Center, lw);

			var half = new Vector3D(node.Size * 0.5 * s);
			localBox = new BoundingBoxD(-half, half);
		}

		public OctreeNode GetNodeAtWorld(Vector3D worldPos)
		{
			var local = Vector3D.Transform(worldPos, MatrixD.Invert(_space.LocalToWorld));
			var cell = new Vector3I(
				(int)Math.Floor(local.X),
				(int)Math.Floor(local.Y),
				(int)Math.Floor(local.Z));
			return GetNodeAt(cell);
		}

		/// <summary>
		/// A* pathfinding on the octree graph.
		/// </summary>
		public List<OctreeNode> FindPath(OctreeNode start, OctreeNode goal)
		{
			var result = new List<OctreeNode>();
			if (start == null || goal == null || start.Type != NodeType.Free || goal.Type != NodeType.Free)
				return result;

			var gScore = new Dictionary<OctreeNode, float>();
			var parent = new Dictionary<OctreeNode, OctreeNode>();
			var closed = new HashSet<OctreeNode>();
			var inOpen = new HashSet<OctreeNode>();
			var items = new Dictionary<OctreeNode, FPQNode>();
			var open = new FastPriorityQueue<FPQNode>(10*1024);

			gScore[start] = 0f;
			var startItem = new FPQNode { Node = start };
			items[start] = startItem;
			inOpen.Add(start);
			open.Enqueue(startItem, Heuristic(start, goal));

			while (open.Count > 0)
			{
				var current = open.Dequeue().Node;

				if (closed.Contains(current)) continue;

				if (current == goal)
				{
					ReconstructPath(parent, goal, result);
					return result;
				}

				closed.Add(current);
				inOpen.Remove(current);

				if (current.Neighbors == null)
					ComputeNeighbors(current);

				foreach (var neighbor in current.Neighbors)
				{
					if (closed.Contains(neighbor)) continue;

					var tentativeG = gScore[current] + (float)Vector3D.Distance(current.Center, neighbor.Center);

					if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
					{
						gScore[neighbor] = tentativeG;
						parent[neighbor] = current;

						float f = tentativeG + Heuristic(neighbor, goal);
						if (inOpen.Contains(neighbor))
						{
							open.UpdatePriority(items[neighbor], f);
						}
						else
						{
							var item = new FPQNode { Node = neighbor };
							items[neighbor] = item;
							inOpen.Add(neighbor);
							open.Enqueue(item, f);
						}
					}
				}
			}

			return result;
		}

		private float Heuristic(OctreeNode node, OctreeNode goal)
		{
			return (float)Vector3D.Distance(node.Center, goal.Center);
		}

		private void ReconstructPath(Dictionary<OctreeNode, OctreeNode> parent, OctreeNode goal, List<OctreeNode> result)
		{
			var current = goal;
			while (current != null)
			{
				result.Add(current);
				if (!parent.TryGetValue(current, out current)) break;
			}
			result.Reverse();
		}

		public Vector3D NodeCenterWorld(OctreeNode node)
		{
			return Vector3D.Transform(node.Center, _space.LocalToWorld);
		}

		public void DrawNode(OctreeNode node, bool marker)
		{
			Color color;
			switch(node.Type)
			{	case NodeType.Free: color = Color.Green; break;
				case NodeType.Blocked: color = Color.Red; break;
				default: color = Color.Gray; break;
			}

			MatrixD matrix;
			BoundingBoxD localBb;
			NodeToWorldBox(node, out matrix, out localBb);

			if(marker) Drawing.RoundMarker(matrix.Translation, color);

			localBb.Min *= 0.98;
			localBb.Max *= 0.98;

			var material = MyStringId.GetOrCompute("Square");
			MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref localBb, ref color,
				MySimpleObjectRasterizer.Wireframe, 1, 0.003f, material, material);
		}
	}
}
