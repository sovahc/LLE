using System;
using System.Collections.Generic;
using System.Linq;

using VRageMath;
using VRage.Voxels;
using Sandbox.Game.Entities;

using Priority_Queue;
using VRage.Utils;

// MyVoxelConstants.VOXEL_SIZE_IN_METRES

namespace LLE
{
	public enum NodeType { Free, Blocked, Mixed }

	/// <summary>
	/// Octree node. Can be a large free zone (super-cell) or a small block.
	/// </summary>
	public class OctNode
	{
		public Vector3I Min;
		public int Size;
		public NodeType Type;
		
		public HashSet<OctNode> Neighbors = new HashSet<OctNode>();
		public OctNode[] Children;

		public Vector3D Center => new Vector3D(Min.X + Size / 2.0, Min.Y + Size / 2.0, Min.Z + Size / 2.0);
	}

	class OctNodeItem : FastPriorityQueueNode
	{
		public OctNode Node;
	}

	public class AsteroidNavigation
	{
		private readonly MyVoxelBase _voxel;
		private OctNode _root;

		public int FreeNodes { get; private set; }
		public int BlockedNodes { get; private set; }
		public int MixedNodes { get; private set; }

		public OctNode Root => _root;

		public AsteroidNavigation(MyVoxelBase voxel)
		{
			_voxel = voxel;
		}

		public void Build(Vector3I min, Vector3I max, int coarseLod = 3) // 3 = 8m
		{
			FreeNodes = BlockedNodes = MixedNodes = 0;

			Vector3I size = max - min;
			var maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));

			var size1d = NextPowerOfTwo(maxDimension);
			_root = new OctNode { Min = min, Size = size1d };

			if (_voxel == null || _voxel.MarkedForClose) return;

			using (_voxel.Pin()) // Do I need this?
				Subdivide(_root, coarseLod);

			BuildNeighborGraph();
			CountLeafNodes(_root);
		}

		private int CalculateLodLevel(int size)
		{	int lod = 0;
			while (size > 1) { size >>= 1; ++lod; }
			return lod;
		}

		private void Subdivide(OctNode node, int coarseLod)
		{
			int lod = CalculateLodLevel(node.Size);
			lod = Math.Min(lod, coarseLod);

			// Get voxel AABB for this node at the current LOD
			var voxelMin = node.Min;
			var voxelMax = node.Min + node.Size - 1;

			node.Type = VoxelCellType(_voxel, voxelMin, voxelMax, lod);

			Utilities.Log($"VoxelCellType {node.Min} / {voxelMin}-{voxelMax} / {lod} / {node.Type}");

			if (node.Size <= 1) return; // leaf node

			if (node.Type != NodeType.Mixed)
			{	if(lod <= coarseLod) return; // supercell
			}

			var half = node.Size / 2;
			node.Children = new OctNode[8];
			
			for (int i = 0; i < 8; ++i)
			{
				var childMin = new Vector3I(
					node.Min.X + ((i & 1) == 0 ? 0 : half),
					node.Min.Y + ((i & 2) == 0 ? 0 : half),
					node.Min.Z + ((i & 4) == 0 ? 0 : half));
				
				var child = new OctNode { Min = childMin, Size = half };
				node.Children[i] = child;
				Subdivide(child, coarseLod);
			}
		}

		/// <summary>
		/// Connects free nodes into a graph using Face Adjacency.
		/// Correctly connects large super-cells with multiple small nodes.
		/// </summary>
		private void BuildNeighborGraph()
		{
			var freeNodes = new List<OctNode>();
			CollectFreeNodes(_root, freeNodes);

			// Search neighbors only in 3 positive directions (X+, Y+, Z+) to avoid duplicate checks. Links are added bidirectionally.
			foreach (var node in freeNodes)
			{
				for (int axis = 0; axis < 3; axis++)
				{
					var neighbors = new List<OctNode>();
					FindFaceNeighbors(_root, node, axis, neighbors);
			
					foreach (var neighbor in neighbors)
					{
						// Bidirectional link for an undirected graph
						node.Neighbors.Add(neighbor);
						neighbor.Neighbors.Add(node); 
					}
				}
			}
		}

		/// <summary>
		/// Recursively finds all free nodes touching the face of target along the given axis (in +1 direction).
		/// </summary>
		private void FindFaceNeighbors(OctNode current, OctNode target, int axis, List<OctNode> results)
		{
			// Prune branches that cannot physically contain neighbors
			if (!CanContainFaceNeighbor(current, target, axis))
				return;

			if (current.Type == NodeType.Free)
			{
				// Found a leaf node. Check for exact face contact.
				if (IsExactFaceNeighbor(current, target, axis))
				{
					results.Add(current);
				}
				return;
			}

			if (current.Type == NodeType.Blocked)
				return; // No free paths in blocked zones

			// Mixed - descend to children
			if (current.Children != null)
			{
				foreach (var child in current.Children)
				{
					FindFaceNeighbors(child, target, axis, results);
				}
			}
		}

		/// <summary>
		/// Check: can the subtree rooted at curr contain a node touching the face of target?
		/// </summary>
		private bool CanContainFaceNeighbor(OctNode curr, OctNode target, int axis)
		{
			int axis1 = (axis + 1) % 3;
			int axis2 = (axis + 2) % 3;

			// 1. Must overlap on both cross-axes
			if (curr.Min[axis1] >= target.Min[axis1] + target.Size) return false;
			if (curr.Min[axis1] + curr.Size <= target.Min[axis1]) return false;

			if (curr.Min[axis2] >= target.Min[axis2] + target.Size) return false;
			if (curr.Min[axis2] + curr.Size <= target.Min[axis2]) return false;

			// 2. Node curr must "cover" the contact plane
			int boundary = target.Min[axis] + target.Size;
			if (curr.Min[axis] > boundary) return false;
			if (curr.Min[axis] + curr.Size <= boundary) return false;

			return true;
		}

		/// <summary>
		/// Strict check: does the leaf node neighbor actually touch the face of target?
		/// </summary>
		private bool IsExactFaceNeighbor(OctNode neighbor, OctNode target, int axis)
		{
			int axis1 = (axis + 1) % 3;
			int axis2 = (axis + 2) % 3;

			// Overlap on cross-axes (must share area)
			if (neighbor.Min[axis1] >= target.Min[axis1] + target.Size) return false;
			if (neighbor.Min[axis1] + neighbor.Size <= target.Min[axis1]) return false;
			if (neighbor.Min[axis2] >= target.Min[axis2] + target.Size) return false;
			if (neighbor.Min[axis2] + neighbor.Size <= target.Min[axis2]) return false;

			// Exact contact along the main axis
			return neighbor.Min[axis] == target.Min[axis] + target.Size;
		}

		private void CollectFreeNodes(OctNode node, List<OctNode> result)
		{
			if (node.Type == NodeType.Free)
			{
				result.Add(node);
			}
			else if (node.Children != null)
			{
				foreach (var child in node.Children)
					CollectFreeNodes(child, result);
			}
		}

		/// <summary>
		/// Finds the node containing a point. Traverses from root down.
		/// </summary>
		public OctNode FindNodeAt(Vector3I pos)
		{
			var current = _root;
			while (current.Type == NodeType.Mixed && current.Children != null)
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
		/// A* pathfinding on the octree graph.
		/// </summary>
		public List<OctNode> FindPath(OctNode start, OctNode goal)
		{
			var result = new List<OctNode>();
			if (start == null || goal == null || start.Type != NodeType.Free || goal.Type != NodeType.Free)
				return result;

			var gScore = new Dictionary<OctNode, float>();
			var parent = new Dictionary<OctNode, OctNode>();
			var closed = new HashSet<OctNode>();
			var inOpen = new HashSet<OctNode>();
			var items = new Dictionary<OctNode, OctNodeItem>();
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

		private float Heuristic(OctNode node, OctNode goal)
		{
			return (float)Vector3D.Distance(node.Center, goal.Center);
		}

		private void ReconstructPath(Dictionary<OctNode, OctNode> parent, OctNode goal, List<OctNode> result)
		{
			var current = goal;
			while (current != null)
			{
				result.Add(current);
				if (!parent.TryGetValue(current, out current)) break;
			}
			result.Reverse();
		}

		private int NextPowerOfTwo(int value)
		{
			int res = 1;
			while (res < value) res *= 2;
			return res;
		}

		private void CountLeafNodes(OctNode node)
		{
			if (node.Children == null)
			{
				switch (node.Type)
				{
					case NodeType.Free: ++FreeNodes; break;
					case NodeType.Blocked: ++BlockedNodes; break;
					case NodeType.Mixed: ++MixedNodes; break;
				}
			}
			else
			{
				foreach (var child in node.Children)
					CountLeafNodes(child);
			}
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

		/// <summary>
		/// Converts an octree node (storage indices) to a world-space bounding box.
		/// </summary>
		public BoundingBoxD NodeToWorldBB(OctNode node)
		{
			var min = new Vector3D(node.Min) + _voxel.PositionLeftBottomCorner;
			var max = min + node.Size;
			return new BoundingBoxD(min, max);
		}

		public MyVoxelBase Voxel => _voxel;

		public OctNode FindNodeAtWorld(Vector3D worldPos)
		{
			var voxelPos = new Vector3I(
				(int)Math.Floor(worldPos.X - _voxel.PositionLeftBottomCorner.X),
				(int)Math.Floor(worldPos.Y - _voxel.PositionLeftBottomCorner.Y),
				(int)Math.Floor(worldPos.Z - _voxel.PositionLeftBottomCorner.Z));
			return FindNodeAt(voxelPos);
		}
	}
}
