using System;
using System.Collections.Generic;

using VRageMath;
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
		
		public HashSet<OctreeNode> Neighbors = new HashSet<OctreeNode>();
		public OctreeNode[] Children;

		public Vector3D Center => new Vector3D(Min.X + Size / 2.0, Min.Y + Size / 2.0, Min.Z + Size / 2.0);
	}

	class OctNodeItem : FastPriorityQueueNode
	{
		public OctreeNode Node;
	}

	public class AsteroidNavigation
	{
		private readonly MyVoxelBase _voxel;
		private readonly int _coarseLod;
		private readonly OctreeNode _root;

		public struct Statistic_
		{	public int Unknown, Top, Free, Mixed, Blocked;

			public override string ToString() => $"Unknown={Unknown} Top={Top} Free={Free} Mixed={Mixed} Blocked={Blocked}";
		}

		public Statistic_ Statistic;
		
		public AsteroidNavigation(MyVoxelBase voxel)
		{
			_voxel = voxel;
			_coarseLod = 4; // 3 = 8m

			Vector3I size = voxel.Storage.Size;
			var maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));
			// in-game voxels are power-of-two sized - Debug.Assert(source.Size3D.IsPowerOfTwo)

			_root = new OctreeNode { Min = Vector3I.Zero, Size = maxDimension, Type = NodeType.Unknown };
			++Statistic.Unknown;
		}

		private int CalculateLodLevel(int size)
		{	int lod = 0;
			while (size > 1) { size >>= 1; ++lod; }
			return lod;
		}

		public OctreeNode GetNodeAt(Vector3I pos)
		{
			if (_voxel == null || _voxel.MarkedForClose) return null;

			var current = _root;

			for(;;)
			{	if(current.Type == NodeType.Unknown)
				{
					--Statistic.Unknown;

					int lod = CalculateLodLevel(current.Size);

					if (lod >= _coarseLod)
					{	current.Type = NodeType.Top;
					}
					else
					{	var voxelMin = current.Min;
						var voxelMax = current.Min + current.Size - 1;
						current.Type = VoxelCellType(_voxel, voxelMin, voxelMax, lod-1);

						Utilities.Log($"VoxelCellType {current.Min} / {voxelMin}-{voxelMax} / {lod} / {current.Type}");
					}

					switch(current.Type)
					{	case NodeType.Top: ++Statistic.Top; break;
						case NodeType.Free: ++Statistic.Free; break;
						case NodeType.Mixed: ++Statistic.Mixed; break;
						case NodeType.Blocked: ++Statistic.Blocked; break;
					}

					if (lod <= 1) return current; // leaf node

					if (current.Type == NodeType.Free ||
						current.Type == NodeType.Blocked) return current; // super-cell
				}

				if(current.Type != NodeType.Mixed &&
					current.Type != NodeType.Top) return current;
				
				if(current.Children == null)
					current.Children = new OctreeNode[8];

				var half = current.Size / 2;
				bool x = pos.X >= current.Min.X + half;
				bool y = pos.Y >= current.Min.Y + half;
				bool z = pos.Z >= current.Min.Z + half;
			
				int index = (x ? 1 : 0) | (y ? 2 : 0) | (z ? 4 : 0);

				if(current.Children[index] == null)
				{	
					var childMin = new Vector3I(
						current.Min.X + (x ? half : 0),
						current.Min.Y + (y ? half : 0),
						current.Min.Z + (z ? half : 0));
					
					current.Children[index] = new OctreeNode { Min = childMin, Size = half, Type = NodeType.Unknown };
					++Statistic.Unknown;
				}

				current = current.Children[index];
			}
		}

		/// <summary>
		/// Finds the node containing a point. Traverses from root down.
		/// </summary>
		public OctreeNode FindNodeAt(Vector3I pos)
		{
			var current = _root;
			while (current.Children != null)
			{
				var half = current.Size / 2;
				bool x = pos.X >= current.Min.X + half;
				bool y = pos.Y >= current.Min.Y + half;
				bool z = pos.Z >= current.Min.Z + half;
				
				int index = (x ? 1 : 0) | (y ? 2 : 0) | (z ? 4 : 0);
				current = current.Children[index];
			}
			return current;
		}

		/// <summary>
		/// Converts an octree node (storage indices) to a world-space bounding box.
		/// </summary>
		public BoundingBoxD NodeToWorldBB(OctreeNode node)
		{
			var min = new Vector3D(node.Min) + _voxel.PositionLeftBottomCorner;
			var max = min + node.Size;
			return new BoundingBoxD(min, max);
		}

		public OctreeNode GetNodeAtWorld(Vector3D worldPos)
		{
			var voxelPos = new Vector3I(
				(int)Math.Floor(worldPos.X - _voxel.PositionLeftBottomCorner.X),
				(int)Math.Floor(worldPos.Y - _voxel.PositionLeftBottomCorner.Y),
				(int)Math.Floor(worldPos.Z - _voxel.PositionLeftBottomCorner.Z));
			return GetNodeAt(voxelPos);
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
			var items = new Dictionary<OctreeNode, OctNodeItem>();
			var open = new FastPriorityQueue<OctNodeItem>(10*1024);

			gScore[start] = 0f;
			var startItem = new OctNodeItem { Node = start };
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
							var item = new OctNodeItem { Node = neighbor };
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

		private static readonly MyStorageData storage = new MyStorageData();

		public static NodeType VoxelCellType(MyVoxelBase voxel, Vector3I min, Vector3I max, int lod = 0)
		{
			Vector3I storageMax = voxel.Storage.Size - 1;
			Vector3I coordMin = min >> lod;
			Vector3I coordMax = max >> lod;

			//Vector3I.Clamp(ref coordMin, ref Vector3I.Zero, ref storageMax, out coordMin);
			//Vector3I.Clamp(ref coordMax, ref Vector3I.Zero, ref storageMax, out coordMax);

			storage.Resize(coordMin, coordMax);
			voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.Material, lod, coordMin, coordMax);

			Utilities.Log(storage.ToBase64());
		
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
}
