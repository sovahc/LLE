using System;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;
using Priority_Queue;

namespace LLE
{
	// TODO Warning: Clunker code, not checked.

	class ConveyorAStar
	{
		private const float BendPenalty = 0.4f;

		private readonly IMyCubeGrid grid;
		private readonly Indexer indexer;
		private readonly Vector3I origin;

		private readonly Vector3I start, goal;
		private readonly List<Base6Directions.Direction> startPorts, goalPorts;

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

		public ConveyorAStar(IMyCubeGrid grid_, Vector3I start_, Vector3I goal_,
			List<Base6Directions.Direction> startPorts_, List<Base6Directions.Direction> goalPorts_)
		{
			grid = grid_;
			start = start_;
			goal = goal_;
			startPorts = startPorts_;
			goalPorts = goalPorts_;

			origin = grid.Min;
			indexer = new Indexer(grid.Max - grid.Min + Vector3I.One);

			int c = indexer.Count;

			closed = new BitField(c, 1);
			inOpen = new BitField(c, 1);
			occupancy = new BitField(c, 2);
			open = new FastPriorityQueue<MyNode>(c);

			gScore = new float[c];
			parent = new int[c];
			nodes = new MyNode[c]; // filled lazily: a full ship's box is millions of cells

			for (int i = 0; i < c; ++i) parent[i] = -1;

			MyConsole.Add($"ConveyorAStar '{grid.DisplayName}' {start} -> {goal} box {indexer.Size} ({c} cells)");

			iterator = FindPath();
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
			Vector3I startLocal = start - origin;
			Vector3I goalLocal = goal - origin;

			if (!indexer.In(startLocal) || !indexer.In(goalLocal))
			{	MyConsole.Add("ConveyorAStar: an endpoint fell outside the search box", Color.Red);
				yield break;
			}

			int startIndex = indexer.Index(startLocal);
			int goalIndex = indexer.Index(goalLocal);

			gScore[startIndex] = 0f;
			open.Enqueue(Node(startIndex), Manhattan(start, goal));
			inOpen.Set(startIndex, 1);

			int expanded = 0;

			while (open.Count > 0)
			{
				++expanded;
				if (expanded % 200 == 0) yield return null;

				var current = open.Dequeue();
				int currentI = current.Index;

				if (closed.Get(currentI) != 0) continue;
				closed.Set(currentI, 1);

				if (currentI == goalIndex)
				{	Reconstruct(goalIndex);
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

					// Leaving the source block: only through one of its ports.
					if (currentI == startIndex && !ConveyorPorts.Contains(startPorts, direction))
						continue;

					if (nextI == goalIndex)
					{	// Entering the target block: it must have a port looking back at us.
						if (!ConveyorPorts.Contains(goalPorts, Base6Directions.GetFlippedDirection(direction)))
							continue;
					}
					else if (!Free(nextCell, nextI)) continue;

					float tentativeG = currentG + 1f;
					if (incoming != Vector3I.Zero && incoming != step)
						tentativeG += BendPenalty;

					if (parent[nextI] != -1 && tentativeG >= gScore[nextI]) continue;

					gScore[nextI] = tentativeG;
					parent[nextI] = currentI;

					float f = tentativeG + Manhattan(nextCell, goal);

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

		private static float Manhattan(Vector3I a, Vector3I b)
		{
			return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
		}
	}
}
