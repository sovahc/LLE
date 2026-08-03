using System;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;
using Priority_Queue;

namespace LLE
{
	class ConveyorAStar
	{
		private const float BendPenalty = 0.4f;
		private const int Border = 10;

		private readonly IMyCubeGrid grid;
		private readonly Indexer indexer;
		private readonly Vector3I origin;

		private readonly Dictionary<int, List<Base6Directions.Direction>> startCells;
		private readonly Dictionary<int, List<Base6Directions.Direction>> goalCells;
		private readonly List<Vector3I> goalPositions;

		private readonly BitField closed;
		private readonly BitField inOpen;
		private readonly BitField occupancy; // 0 unknown, 1 free, 2 blocked
		private readonly FastPriorityQueue<MyNode> open;
		private readonly float[] gScore;
		private readonly int[] parent;
		private readonly MyNode[] nodes;

		private IEnumerator iterator;

		public readonly List<Vector3I> Result = new List<Vector3I>();

		// Same order as Constants.SixDirections, so the two are indexed together. Spelled out
		// rather than derived, because Base6Directions.Forward is -Z and a naive conversion
		// silently flips the Z sign of every direction it touches.
		private static readonly Base6Directions.Direction[] SixAsDirections =
		{	Base6Directions.Direction.Left,    Base6Directions.Direction.Right,
			Base6Directions.Direction.Down,    Base6Directions.Direction.Up,
			Base6Directions.Direction.Forward, Base6Directions.Direction.Backward
		};

		public ConveyorAStar(IMyCubeGrid grid_,
			List<ConveyorPort> startPorts_, List<ConveyorPort> goalPorts_)
		{
			grid = grid_;

			// Bounding box: all port cells + a fixed border, clamped to the grid.
			Vector3I lo = new Vector3I(int.MaxValue), hi = new Vector3I(int.MinValue);
			foreach (var p in startPorts_) { lo = Vector3I.Min(lo, p.Cell); hi = Vector3I.Max(hi, p.Cell); }
			foreach (var p in goalPorts_)  { lo = Vector3I.Min(lo, p.Cell); hi = Vector3I.Max(hi, p.Cell); }
			lo = Vector3I.Max(lo - new Vector3I(Border), grid.Min);
			hi = Vector3I.Min(hi + new Vector3I(Border), grid.Max);

			origin = lo;
			indexer = new Indexer(hi - lo + Vector3I.One);

			int c = indexer.Count;

			closed = new BitField(c, 1);
			inOpen = new BitField(c, 1);
			occupancy = new BitField(c, 2);
			open = new FastPriorityQueue<MyNode>(c);

			gScore = new float[c];
			parent = new int[c];
			nodes = new MyNode[c]; // filled lazily: a full ship's box is millions of cells

			for (int i = 0; i < c; ++i) parent[i] = -1;

			startCells = BuildPortMap(startPorts_);
			goalCells = BuildPortMap(goalPorts_);

			goalPositions = new List<Vector3I>();
			foreach (var entry in goalCells)
			{	Vector3I local;
				indexer.IndexToPosition(entry.Key, out local);
				goalPositions.Add(local + origin);
			}

			MyConsole.Add($"ConveyorAStar '{grid.DisplayName}' {startCells.Count} -> {goalCells.Count} ports, box {indexer.Size} ({c} cells)");

			iterator = FindPath();
		}

		private Dictionary<int, List<Base6Directions.Direction>> BuildPortMap(List<ConveyorPort> ports)
		{
			var map = new Dictionary<int, List<Base6Directions.Direction>>();
			foreach (var p in ports)
			{	Vector3I local = p.Cell - origin;
				if (!indexer.In(local)) continue;
				int idx = indexer.Index(local);
				List<Base6Directions.Direction> dirs;
				if (!map.TryGetValue(idx, out dirs))
				{	dirs = new List<Base6Directions.Direction>();
					map[idx] = dirs;
				}
				if (!ConveyorPorts.Contains(dirs, p.Direction))
					dirs.Add(p.Direction);
			}
			return map;
		}

		public bool Tick()
		{
			if (iterator == null) return true;
			Utilities.Tick(ref iterator, "ConveyorAStar");
			return iterator == null;
		}

		private bool Free(Vector3I cell, int index)
		{
			byte known = occupancy.Get(index);
			if (known != 0) return known == 1;

			bool free = grid.GetCubeBlock(cell) == null;
			occupancy.Set(index, (byte)(free ? 1 : 2));
			return free;
		}

		private MyNode Node(int index)
		{
			var n = nodes[index];
			if (n == null)
			{	n = new MyNode { Index = index };
				nodes[index] = n;
			}
			return n;
		}

		private IEnumerator FindPath()
		{
			// Seed every start cell with g = 0.
			foreach (var entry in startCells)
			{
				int idx = entry.Key;
				gScore[idx] = 0f;
				parent[idx] = -1;
				Vector3I local;
				indexer.IndexToPosition(idx, out local);
				open.Enqueue(Node(idx), Heuristic(local + origin));
				inOpen.Set(idx, 1);
			}

			int expanded = 0;

			while (open.Count > 0)
			{
				++expanded;
				if (expanded % 200 == 0) yield return null;

				var current = open.Dequeue();
				int currentI = current.Index;

				if (closed.Get(currentI) != 0) continue;
				closed.Set(currentI, 1);

				if (goalCells.ContainsKey(currentI))
				{	Reconstruct(currentI);
					MyConsole.Add($"ConveyorAStar: found, {expanded} cells analysed, {Result.Count} long");
					yield break;
				}

				Vector3I currentLocal;
				indexer.IndexToPosition(currentI, out currentLocal);
				Vector3I currentCell = currentLocal + origin;

				Vector3I incoming = Vector3I.Zero;
				if (parent[currentI] != -1)
				{	Vector3I parentLocal;
					indexer.IndexToPosition(parent[currentI], out parentLocal);
					incoming = currentLocal - parentLocal;
				}

				float currentG = gScore[currentI];

				for (int d = 0; d < Constants.SixDirections.Length; ++d)
				{
					Vector3I step = Constants.SixDirections[d];
					Vector3I nextLocal = currentLocal + step;

					if (!indexer.In(nextLocal)) continue;

					int nextI = indexer.Index(nextLocal);
					if (closed.Get(nextI) != 0) continue;

					Vector3I nextCell = nextLocal + origin;
					var direction = SixAsDirections[d];

					// Leaving a start cell: only through one of its ports.
					List<Base6Directions.Direction> exitDirs;
					if (startCells.TryGetValue(currentI, out exitDirs) &&
						!ConveyorPorts.Contains(exitDirs, direction))
						continue;

					// Entering a goal cell: it must have a port looking back at us.
					List<Base6Directions.Direction> entryDirs;
					if (goalCells.TryGetValue(nextI, out entryDirs))
					{	if (!ConveyorPorts.Contains(entryDirs, Base6Directions.GetFlippedDirection(direction)))
							continue;
					}
					else if (!Free(nextCell, nextI)) continue;

					float tentativeG = currentG + 1f;
					if (incoming != Vector3I.Zero && incoming != step)
						tentativeG += BendPenalty;

					if (parent[nextI] != -1 && tentativeG >= gScore[nextI]) continue;

					gScore[nextI] = tentativeG;
					parent[nextI] = currentI;

					float f = tentativeG + Heuristic(nextCell);

					if (inOpen.Get(nextI) != 0)
						open.UpdatePriority(Node(nextI), f);
					else
					{	open.Enqueue(Node(nextI), f);
						inOpen.Set(nextI, 1);
					}
				}
			}

			MyConsole.Add($"ConveyorAStar: no route, {expanded} cells analysed", Color.Red);
		}

		private void Reconstruct(int goalIndex)
		{
			Result.Clear();

			int i = goalIndex;
			while (i != -1)
			{	Vector3I local;
				indexer.IndexToPosition(i, out local);
				Result.Add(local + origin);
				i = parent[i];
			}

			Result.Reverse();
		}

		private float Heuristic(Vector3I cell)
		{
			float min = float.MaxValue;
			for (int i = 0; i < goalPositions.Count; ++i)
			{	float d = Math.Abs(cell.X - goalPositions[i].X)
					   + Math.Abs(cell.Y - goalPositions[i].Y)
					   + Math.Abs(cell.Z - goalPositions[i].Z);
				if (d < min) min = d;
			}
			return min;
		}
	}
}
