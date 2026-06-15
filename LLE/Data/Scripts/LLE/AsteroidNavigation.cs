using System;
using System.Collections.Generic;

using VRageMath;
using Sandbox.Game.Entities;

using Priority_Queue;

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
		
		public List<OctNode> Neighbors = new List<OctNode>();
		public OctNode[] Children;

		public Vector3D Center => new Vector3D(Min.X + Size / 2.0, Min.Y + Size / 2.0, Min.Z + Size / 2.0);

		public BoundingBoxD WorldAABB(Vector3D voxelCorner)
		{
			var min = voxelCorner + Min * 2.0;
			var max = voxelCorner + (Min + Size) * 2.0;
			return new BoundingBoxD(min, max);
		}
	}

	class OctNodeItem : FastPriorityQueueNode
	{
		public OctNode Node;
	}

	public class AsteroidNavigation
	{
		private const int MinNodeSize = 1; // Minimum node size (1 "unit" = 2 meters)
		
		private readonly MyVoxelBase _voxel;
		private OctNode _root;
		
		public OctNode Root => _root;

		public AsteroidNavigation(MyVoxelBase voxel)
		{
			_voxel = voxel;
		}

		/// <summary>
		/// Builds an octree for the given bounding box (in 2-meter units relative to the voxel corner).
		/// </summary>
		public void Build(Vector3I min, Vector3I max)
		{
			var size = GetPowerOfTwo(Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)));
			_root = new OctNode { Min = min, Size = size };

			Subdivide(_root);
			BuildNeighborGraph();
		}

		private void Subdivide(OctNode node)
		{
			var aabb = node.WorldAABB(_voxel.PositionLeftBottomCorner);

			// 1. If node is smaller than minimum size - it's a leaf
			if (node.Size <= MinNodeSize)
			{
				node.Type = TraversabilityCalculator.HasMaterialsInBox(aabb, _voxel) ? NodeType.Blocked : NodeType.Free;
				return;
			}

			// 2. If node is completely free - it's a super-cell!
			if (!TraversabilityCalculator.HasMaterialsInBox(aabb, _voxel))
			{
				node.Type = NodeType.Free;
				return;
			}

			// 3. Otherwise, divide into 8
			node.Type = NodeType.Mixed;
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
				Subdivide(child);
			}
		}

		/// <summary>
		/// Connects free nodes into a graph.
		/// For each free node, we look for neighbors in 6 directions.
		/// </summary>
		private void BuildNeighborGraph()
		{
			var freeNodes = new List<OctNode>();
			CollectFreeNodes(_root, freeNodes);

			// Only check positive directions to avoid duplicates and O(N) Contains checks
			var dirs = new[] { 
				new Vector3I(1,0,0), 
				new Vector3I(0,1,0), 
				new Vector3I(0,0,1) 
			};

			foreach (var node in freeNodes)
			{
				foreach (var dir in dirs)
				{
					var targetPos = node.Min + dir * node.Size;
					var neighbor = FindNodeAt(targetPos);
					
					if (neighbor != null && neighbor.Type == NodeType.Free)
					{
						node.Neighbors.Add(neighbor);
						neighbor.Neighbors.Add(node);
					}
				}
			}
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

		private int GetPowerOfTwo(int value)
		{
			int res = 1;
			while (res < value) res *= 2;
			return res;
		}
	}
}
