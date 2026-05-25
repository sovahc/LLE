using System;
using System.Collections;
using System.Collections.Generic;
using VRageMath;
using Priority_Queue;

namespace LLE
{
	public class BitField
	{
		private readonly long[] _data;
		private readonly int _bits, _mask;

		public BitField(int count, int bits)
		{
			if (bits != 1 && bits != 2 && bits != 4)
				throw new ArgumentException("Only 1, 2 or 4 bits are supported.");

			_bits = bits;
			_mask = (1 << bits) - 1;
			_data = new long[(count * bits + 63) >> 6];
		}

		public void Set(int index, byte value)
		{
			int pos = index * _bits;
			int word = pos >> 6;
			int shift = pos & 63;

			long mask = ~((long)_mask << shift);
			_data[word] = (_data[word] & mask) | ((long)(value & _mask) << shift);
		}

		public byte Get(int index)
		{
			int pos = index * _bits;
			return (byte)((_data[pos >> 6] >> (pos & 63)) & _mask);
		}

		public void SetAll_0()
		{
			Array.Clear(_data, 0, _data.Length);
		}

		public void SetAll_1()
		{	
			for(int i = 0; i < _data.Length; ++i)
				_data[i] = -1;
		}
	}

	class Indexer
	{	
		public readonly Vector3I Size;
		public readonly int Count;

		public Indexer(Vector3I size)
		{	Size = size;
			Count = size.Size;
		}

		public int Index(int x, int y, int z)
		{	return x + y * Size.X + z * Size.X * Size.Y;
		}

		public int Index(Vector3I v)
		{	return Index(v.X, v.Y, v.Z);
		}

		public int Index(int index, int dx, int dy, int dz)
		{	return index + dx + dy * Size.X + dz * Size.X * Size.Y;
		}

		public bool In(int x, int y, int z)
		{	return x >= 0 && x < Size.X &&
				y >= 0 && y < Size.Y &&
				z >= 0 && z < Size.Z;
		}

		public bool In(Vector3I v)
		{	return In(v.X, v.Y, v.Z);
		}

		public void IndexToPosition(int index, out Vector3I v)
		{	v.Z = index / (Size.X * Size.Y);
			index -= v.Z * Size.X * Size.Y;
			v.Y = index / Size.X;
			index -= v.Y * Size.X;
			v.X = index;
		}
	}

	public class MyNode : FastPriorityQueueNode
	{
		public int Index;
	}

	class AStar
	{
		private readonly Indexer _indexer;

		private readonly BitField _closed;
		private readonly BitField _inOpen;
		private readonly FastPriorityQueue<MyNode> _open;
		
		private readonly Traversability[] _traversability;
		private readonly float[] _gScore;
		private readonly int[] _parent;
		private readonly MyNode[] _nodes;

		private IEnumerator iterator;

		public readonly List<Vector3I> result = new List<Vector3I>();

		public Vector3I Size => _indexer.Size;

		private static readonly Vector3I[] Directions = new Vector3I[]
		{
			new Vector3I(1, 0, 0),  new Vector3I(-1, 0, 0),
			new Vector3I(0, 1, 0),  new Vector3I(0, -1, 0),
			new Vector3I(0, 0, 1),  new Vector3I(0, 0, -1),
		};

		public AStar(Vector3I size)
		{
			_indexer = new Indexer(size);
			int c = _indexer.Count;

			_closed = new BitField(c, 1);
			_inOpen = new BitField(c, 1);
			_open = new FastPriorityQueue<MyNode>(c);

			_traversability = new Traversability[c];
			_gScore = new float[c];
			_parent = new int[c];
			_nodes = new MyNode[c];

			for (int i = 0; i < c; i++) _nodes[i] = new MyNode { Index = i };

			for (int i = 0; i < _parent.Length; i++) _parent[i] = -1;
		}

		public void Reset(bool clearTraversability)
		{	
			if(clearTraversability)
				foreach (var t in _traversability)
					t.SetAll(false);

			_closed.SetAll_0();
			_inOpen.SetAll_0();
			_open.Clear();
			
			Array.Clear(_gScore, 0, _gScore.Length);

			for (int i = 0; i < _parent.Length; i++) _parent[i] = -1;
		}

		public void SetTraversability(Vector3I at, Traversability t)
		{	if(!_indexer.In(at)) throw new Exception($"SetTraversability: index out of range: {at}");
			_traversability[_indexer.Index(at)] = t;
		}

		public Traversability GetTraversability(Vector3I at)
		{	if(!_indexer.In(at)) return Traversability.Free;
			return _traversability[_indexer.Index(at)];
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
			if(!_indexer.In(start) || !_indexer.In(goal))
			{	Utilities.Log($"FindPath Error - index out of range: start {start} goal {goal} size {_indexer.Size}");
				yield break;
			}

			int startIndex = _indexer.Index(start.X, start.Y, start.Z);
			int goalIndex = _indexer.Index(goal.X, goal.Y, goal.Z);

			if(_traversability[startIndex].Center || _traversability[goalIndex].Center)
			{	MyConsole.Add($"FindPath Error - start or goal obstructed", Color.Red);
				yield break;
			}

			if (startIndex == goalIndex)
			{
				result.Add(start);
				yield break;
			}

			_gScore[startIndex] = 0f;
			_parent[startIndex] = -1;
			float startF = Manhattan(start, goal);
			_open.Enqueue(_nodes[startIndex], startF);
			_inOpen.Set(startIndex, 1);

			int cellsAnalyzed = 0;

			while (_open.Count > 0)
			{
				if(cellsAnalyzed % 200 == 0) yield return null;

				var current = _open.Dequeue();
				int currentI = current.Index;

				if (_closed.Get(currentI) != 0) continue;
				
				_closed.Set(currentI, 1);

				Vector3I cv;
				_indexer.IndexToPosition(currentI, out cv);

				if (currentI == goalIndex)
				{	MyConsole.Add($"cellsAnalyzed {cellsAnalyzed}", Color.Red);
					result.AddList(ReconstructPath(goalIndex, goal));
					yield break;
				}

				float curG = _gScore[currentI];

				for (int d = 0; d < Directions.Length; ++d)
				{
					var direction = Directions[d];

					Vector3I next = cv + direction;

					if (!_indexer.In(next)) continue;

					++cellsAnalyzed;

					int nextI = _indexer.Index(next);

					if (_traversability[nextI].Center) continue;
					if (_traversability[currentI][direction]) continue;
					if (_traversability[nextI][-direction]) continue;

					if (_closed.Get(nextI) != 0) continue;

					float tentativeG = curG + 1;

					bool isBetter = _parent[nextI] == -1 || tentativeG < _gScore[nextI];

					if (!isBetter) continue;
					
					_gScore[nextI] = tentativeG;
					_parent[nextI] = currentI;

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
