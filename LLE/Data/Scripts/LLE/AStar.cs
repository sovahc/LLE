using System;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using Priority_Queue;

namespace LLE
{
	public class MyNode : FastPriorityQueueNode
	{
		public int Index;
	}

	class AStar
	{
		private readonly Indexer _indexer;
		private readonly TraversabilityCalculator _source;

		private readonly BitField _closed;
		private readonly BitField _inOpen;
		private readonly BitField _known;
		private readonly FastPriorityQueue<MyNode> _open;
		
		private readonly Traversability[] _traversability;
		private readonly float[] _gScore;
		private readonly int[] _parent;
		private readonly MyNode[] _nodes;

		private IEnumerator iterator;

		public readonly List<Vector3I> result = new List<Vector3I>();

		public Vector3I Size => _indexer.Size;
		public void IndexToPosition(int index, out Vector3I pos) => _indexer.IndexToPosition(index, out pos);

		public AStar(Vector3I size, TraversabilityCalculator source)
		{
			_indexer = new Indexer(size);
			_source = source;
			int c = _indexer.Count;

			_closed = new BitField(c, 1);
			_inOpen = new BitField(c, 1);
			_known = new BitField(c, 1);
			_open = new FastPriorityQueue<MyNode>(c);

			_traversability = new Traversability[c];
			_gScore = new float[c];
			_parent = new int[c];
			_nodes = new MyNode[c];

			for (int i = 0; i < c; i++) _nodes[i] = new MyNode { Index = i };

			for (int i = 0; i < _parent.Length; i++) _parent[i] = -1;
		}

		public void Reset()
		{
			_closed.SetAll_0();
			_inOpen.SetAll_0();
			_known.SetAll_0();
			_open.Clear();

			Array.Clear(_gScore, 0, _gScore.Length);

			for (int i = 0; i < _parent.Length; i++) _parent[i] = -1;
		}

		private Traversability GetTraversability(int index)
		{
			if (_known.Get(index) != 0)
				return _traversability[index];

			Vector3I v;
			_indexer.IndexToPosition(index, out v);

			var t = _source.GetForAstar(v);
			_traversability[index] = t;
			_known.Set(index, 1);
			return t;
		}

		public void RunCalculation(Vector3I start, Vector3I goal)
		{	result.Clear();
			iterator = FindPath(start, goal);
		}

		public bool Completed()
		{	return iterator == null;
		}

		public void Iteration() => Utilities.Tick(ref iterator, "AStar");

		public IEnumerator FindPath(Vector3I start, Vector3I goal)
		{
			if(!_indexer.In(start))
			{	MyConsole.Add($"FindPath Error - start index out of range (start {start} size {_indexer.Size})", Color.Red);
				yield break;
			}

			bool goalOutside = !_indexer.In(goal);

			int startIndex = _indexer.Index(start.X, start.Y, start.Z);
			int goalIndex = _indexer.Index(goal.X, goal.Y, goal.Z);

			if(GetTraversability(startIndex).Center)
			{   MyConsole.Add($"FindPath Error - start is obstructed", Color.Red);
				yield break;
			}
			
			if(!goalOutside && GetTraversability(goalIndex).Center)
			{   MyConsole.Add($"FindPath Error - goal is obstructed", Color.Red);
				yield break;
			}

			if (startIndex == goalIndex)
			{
				result.Add(start);
				yield break;
			}

			_gScore[startIndex] = 0f;
			_parent[startIndex] = -1;
			// Heuristic x2: intentional overestimation for speed (sacrifices optimality)
			float startF = Manhattan(start, goal) * 2;
			_open.Enqueue(_nodes[startIndex], startF);
			_inOpen.Set(startIndex, 1);

			int cellsAnalyzed = 0;

			while (_open.Count > 0)
			{
				++cellsAnalyzed;
				if(cellsAnalyzed % 200 == 0) yield return null;

				var current = _open.Dequeue();
				int currentI = current.Index;

				if (_closed.Get(currentI) != 0) continue;

				_closed.Set(currentI, 1);

				Vector3I cv;
				_indexer.IndexToPosition(currentI, out cv);

				if(goalOutside)
				{	
					if(OnBorder(cv))
					{	MyConsole.Add($"(Exit) cellsAnalyzed {cellsAnalyzed}", Color.Red);
						result.AddList(ReconstructPath(currentI, cv));
						yield break;
					}
				}
				else
				{	if (currentI == goalIndex)
					{	MyConsole.Add($"(Goal) cellsAnalyzed {cellsAnalyzed}", Color.Red);
						result.AddList(ReconstructPath(goalIndex, goal));
						yield break;
					}
				}

				float curG = _gScore[currentI];
				var currentT = GetTraversability(currentI);

				int totalDirs = Constants.SixDirections.Length + Constants.TwelveEdgeDirections.Length;
				for (int d = 0; d < totalDirs; ++d)
				{
					Vector3I direction;
					float stepCost;
					if (d < Constants.SixDirections.Length)
					{
						direction = Constants.SixDirections[d];
						stepCost = 1f;
					}
					else
					{
						direction = Constants.TwelveEdgeDirections[d - Constants.SixDirections.Length];
						stepCost = 1.4142f + Constants.AStarDiagonalPenalty;
					}

					Vector3I next = cv + direction;

					if (!_indexer.In(next)) continue;

					int nextI = _indexer.Index(next);

					if (_closed.Get(nextI) != 0) continue;

					var nextT = GetTraversability(nextI);
					if (nextT.Center) continue;
					if (currentT[direction] || nextT[-direction]) continue;

					float tentativeG = curG + stepCost;

					if (_parent[currentI] != -1)
					{
						Vector3I parentPos;
						_indexer.IndexToPosition(_parent[currentI], out parentPos);
						if ((cv - parentPos) != direction)
							tentativeG += Constants.AStarTurnPenalty;
					}

					// Obstacle proximity: penalize cells next to blocked neighbors
					int blockedNeighbors = 0;
					for (int n = 0; n < Constants.SixDirections.Length; n++)
					{
						var neighbor = next + Constants.SixDirections[n];
						if (!_indexer.In(neighbor)) { blockedNeighbors++; continue; }
						if (GetTraversability(_indexer.Index(neighbor)).Center)
							blockedNeighbors++;
					}
					tentativeG += blockedNeighbors * Constants.AStarProximityPenalty;

					bool isBetter = _parent[nextI] == -1 || tentativeG < _gScore[nextI];

					if (!isBetter) continue;

					_gScore[nextI] = tentativeG;
					_parent[nextI] = currentI;

					// Heuristic x2: intentional overestimation for speed (sacrifices optimality)
					float h = Manhattan(next, goal) * 2;
					if (_inOpen.Get(nextI) != 0)
						_open.UpdatePriority(_nodes[nextI], tentativeG + h);
					else
					{
						_open.Enqueue(_nodes[nextI], tentativeG + h);
						_inOpen.Set(nextI, 1);
					}
				}
			}

			yield break;
		}

		private bool OnBorder(Vector3I v)
		{
			if(v.X == 0 || v.Y == 0 || v.Z == 0) return true;
			if(v.X == Size.X-1 || v.Y == Size.Y-1 || v.Z == Size.Z-1) return true;
			return false;
		}

		private List<Vector3I> ReconstructPath(int goalIndex, Vector3I goal)
		{
			var path = new List<Vector3I>();

			int i = goalIndex;
			while (i != -1)
			{
				var v = new Vector3I();
				_indexer.IndexToPosition(i, out v);

				path.Add(v);
				i = _parent[i];
			}

			path.Reverse();
			return path;
		}

		private static float Manhattan(Vector3I a, Vector3I b)
		{
			return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
		}
	}
}
