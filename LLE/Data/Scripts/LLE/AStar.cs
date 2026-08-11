using System;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace LLE
{
	class AStarChunk
	{
		public const int Bits = 4;
		public const int Side = 1 << Bits;
		public const int Mask = Side - 1;
		public const int Cells = Side * Side * Side;
		public const int IndexBits = Bits * 3;
		public const int IndexMask = Cells - 1;

		public const byte Known = 1;
		public const byte Closed = 2;

		public Vector3I Coord;
		public int Slot;

		public readonly Traversability[] traversability = new Traversability[Cells];
		public readonly float[] gScore = new float[Cells];
		public readonly int[] parent = new int[Cells];
		public readonly byte[] flags = new byte[Cells];

		public readonly List<MyVoxelBase> voxels = new List<MyVoxelBase>();
		public readonly List<IMyCubeGrid> grids = new List<IMyCubeGrid>();

		public static int Local(Vector3I cell)
		{	return (cell.X & Mask) | ((cell.Y & Mask) << Bits) | ((cell.Z & Mask) << (Bits * 2));
		}

		public void LocalToCell(int local, out Vector3I cell)
		{	cell.X = (Coord.X << Bits) | (local & Mask);
			cell.Y = (Coord.Y << Bits) | ((local >> Bits) & Mask);
			cell.Z = (Coord.Z << Bits) | (local >> (Bits * 2));
		}
	}

	class AStarHeap
	{
		private readonly float[] _priority;
		private readonly int[] _cell;
		private int _count;

		public AStarHeap(int capacity)
		{	_priority = new float[capacity];
			_cell = new int[capacity];
		}

		public int Count => _count;

		public bool Push(int cell, float priority)
		{
			if (_count == _cell.Length) return false;

			int i = _count++;
			while (i > 0)
			{
				int up = (i - 1) >> 1;
				if (_priority[up] <= priority) break;
				_priority[i] = _priority[up];
				_cell[i] = _cell[up];
				i = up;
			}

			_priority[i] = priority;
			_cell[i] = cell;
			return true;
		}

		public int Pop()
		{
			int top = _cell[0];

			--_count;
			if (_count > 0)
			{
				float priority = _priority[_count];
				int cell = _cell[_count];

				int i = 0;
				while (true)
				{
					int down = i * 2 + 1;
					if (down >= _count) break;
					if (down + 1 < _count && _priority[down + 1] < _priority[down]) ++down;
					if (_priority[down] >= priority) break;

					_priority[i] = _priority[down];
					_cell[i] = _cell[down];
					i = down;
				}

				_priority[i] = priority;
				_cell[i] = cell;
			}

			return top;
		}
	}

	class AStar
	{
		private readonly TraversabilityCalculator _source;
		private readonly Vector3I _boxMin, _boxMax;

		private readonly List<AStarChunk> _chunks = new List<AStarChunk>();
		private readonly Dictionary<Vector3I, AStarChunk> _byCoord =
			new Dictionary<Vector3I, AStarChunk>(Vector3I.Comparer);

		private readonly AStarHeap _open = new AStarHeap(Constants.AStarQueueCapacity);

		private AStarChunk _lastChunk;
		private Vector3I _lastCoord;

		private IEnumerator iterator;

		public readonly List<Vector3I> result = new List<Vector3I>();

		public AStar(Vector3I boxMin, Vector3I boxMax, TraversabilityCalculator source)
		{	_boxMin = boxMin;
			_boxMax = boxMax;
			_source = source;
		}

		public void RunCalculation(Vector3I start, Vector3I goal)
		{	result.Clear();
			iterator = FindPath(start, goal);
		}

		public bool Completed()
		{	return iterator == null;
		}

		public void Iteration() => Utilities.Tick(ref iterator, "AStar");

		private AStarChunk Chunk(Vector3I cell)
		{
			Vector3I coord = cell >> AStarChunk.Bits;
			if (_lastChunk != null && coord == _lastCoord) return _lastChunk;

			AStarChunk chunk;
			if (!_byCoord.TryGetValue(coord, out chunk))
			{
				chunk = new AStarChunk { Coord = coord, Slot = _chunks.Count };
				_chunks.Add(chunk);
				_byCoord.Add(coord, chunk);

				var cellMin = coord << AStarChunk.Bits;
				_source.QueryObstacles(
					_source.CellRangeToWorld(cellMin, cellMin + AStarChunk.Mask),
					chunk.voxels, chunk.grids);
			}

			_lastCoord = coord;
			_lastChunk = chunk;
			return chunk;
		}

		private Traversability GetTraversability(AStarChunk chunk, int local, Vector3I cell)
		{
			if ((chunk.flags[local] & AStarChunk.Known) != 0) return chunk.traversability[local];

			var t = _source.GetForAstar(cell, chunk.voxels, chunk.grids);
			chunk.traversability[local] = t;
			chunk.flags[local] |= AStarChunk.Known;
			return t;
		}

		private Traversability GetTraversability(Vector3I cell)
		{
			var chunk = Chunk(cell);
			return GetTraversability(chunk, AStarChunk.Local(cell), cell);
		}

		private static int Index(AStarChunk chunk, int local)
		{	return (chunk.Slot << AStarChunk.IndexBits) | local;
		}

		private void IndexToCell(int index, out Vector3I cell)
		{	_chunks[index >> AStarChunk.IndexBits].LocalToCell(index & AStarChunk.IndexMask, out cell);
		}

		private bool InBox(Vector3I v)
		{	return v.X >= _boxMin.X && v.X <= _boxMax.X &&
					v.Y >= _boxMin.Y && v.Y <= _boxMax.Y &&
					v.Z >= _boxMin.Z && v.Z <= _boxMax.Z;
		}

		private bool OnBorder(Vector3I v)
		{	return v.X == _boxMin.X || v.Y == _boxMin.Y || v.Z == _boxMin.Z ||
					v.X == _boxMax.X || v.Y == _boxMax.Y || v.Z == _boxMax.Z;
		}

		public IEnumerator FindPath(Vector3I start, Vector3I goal)
		{
			if (!InBox(start))
			{	MyConsole.Add($"FindPath Error - start out of range (start {start} box {_boxMin} {_boxMax})", Color.Red);
				yield break;
			}

			bool goalOutside = !InBox(goal);

			var startChunk = Chunk(start);
			int startLocal = AStarChunk.Local(start);

			var startT = GetTraversability(startChunk, startLocal, start);
			if (startT.Center)
			{	MyConsole.Add($"FindPath Error - start is obstructed", Color.Red);
				yield break;
			}

			if (!goalOutside && GetTraversability(goal).Center)
			{	MyConsole.Add($"FindPath Error - goal is obstructed", Color.Red);
				yield break;
			}

			if (start == goal)
			{
				result.Add(start);
				yield break;
			}

			startChunk.gScore[startLocal] = 0f;
			// Heuristic x2: intentional overestimation for speed (sacrifices optimality)
			_open.Push(Index(startChunk, startLocal), Manhattan(start, goal) * 2);

			int cellsAnalyzed = 0;

			while (_open.Count > 0)
			{
				++cellsAnalyzed;
				if (cellsAnalyzed >= Constants.AStarMaxCells)
				{	MyConsole.Add($"FindPath Error - cell limit reached ({cellsAnalyzed})", Color.Red);
					yield break;
				}
				if (cellsAnalyzed % 200 == 0) yield return null;

				int currentI = _open.Pop();

				var currentChunk = _chunks[currentI >> AStarChunk.IndexBits];
				int currentLocal = currentI & AStarChunk.IndexMask;

				if ((currentChunk.flags[currentLocal] & AStarChunk.Closed) != 0) continue;

				currentChunk.flags[currentLocal] |= AStarChunk.Closed;

				Vector3I cv;
				currentChunk.LocalToCell(currentLocal, out cv);

				if (goalOutside)
				{
					if (OnBorder(cv))
					{	MyConsole.Add($"(Exit) cellsAnalyzed {cellsAnalyzed}", Color.Red);
						ReconstructPath(currentI);
						yield break;
					}
				}
				else
				{	if (cv == goal)
					{	MyConsole.Add($"(Goal) cellsAnalyzed {cellsAnalyzed}", Color.Red);
						ReconstructPath(currentI);
						yield break;
					}
				}

				float curG = currentChunk.gScore[currentLocal];
				var currentT = GetTraversability(currentChunk, currentLocal, cv);

				int currentParent = currentChunk.parent[currentLocal];
				Vector3I incoming = Vector3I.Zero;
				if (currentParent != 0)
				{
					Vector3I parentPos;
					IndexToCell(currentParent - 1, out parentPos);
					incoming = cv - parentPos;
				}

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

					if (!InBox(next)) continue;

					var nextChunk = Chunk(next);
					int nextLocal = AStarChunk.Local(next);

					if ((nextChunk.flags[nextLocal] & AStarChunk.Closed) != 0) continue;

					var nextT = GetTraversability(nextChunk, nextLocal, next);
					if (nextT.Center) continue;
					if (currentT[direction] || nextT[-direction]) continue;

					float tentativeG = curG + stepCost;

					if (currentParent != 0 && incoming != direction)
						tentativeG += Constants.AStarTurnPenalty;

					int blockedNeighbors = 0;
					for (int n = 0; n < Constants.SixDirections.Length; n++)
					{
						var neighbor = next + Constants.SixDirections[n];
						if (!InBox(neighbor)) { blockedNeighbors++; continue; }
						if (GetTraversability(neighbor).Center)
							blockedNeighbors++;
					}
					tentativeG += blockedNeighbors * Constants.AStarProximityPenalty;

					if (nextChunk.parent[nextLocal] != 0 && tentativeG >= nextChunk.gScore[nextLocal]) continue;

					nextChunk.gScore[nextLocal] = tentativeG;
					nextChunk.parent[nextLocal] = currentI + 1;

					// Heuristic x2: intentional overestimation for speed (sacrifices optimality)
					float h = Manhattan(next, goal) * 2;
					if (!_open.Push(Index(nextChunk, nextLocal), tentativeG + h))
					{	MyConsole.Add($"FindPath Error - queue overflow ({Constants.AStarQueueCapacity})", Color.Red);
						yield break;
					}
				}
			}

			MyConsole.Add($"FindPath Error - no path from {start} {startT}", Color.Red);
			yield break;
		}

		private void ReconstructPath(int goalIndex)
		{
			int i = goalIndex + 1;
			while (i != 0)
			{
				Vector3I v;
				IndexToCell(i - 1, out v);
				result.Add(v);

				i = _chunks[(i - 1) >> AStarChunk.IndexBits].parent[(i - 1) & AStarChunk.IndexMask];
			}

			result.Reverse();
		}

		private static float Manhattan(Vector3I a, Vector3I b)
		{
			return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
		}
	}
}
