using System;
using System.Collections.Generic;
using Priority_Queue;
using VRageMath;

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

		public void Clear()
		{
			Array.Clear(_data, 0, _data.Length);
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

	class Map
	{
		private readonly Indexer _indexer;
		private readonly BitField _walkable;
		private readonly BitField _closed;
		private readonly BitField _inOpen;
		private readonly FastPriorityQueue<MyNode> _open;
		private readonly float[] _gScore;
		private readonly int[] _parent;
		private readonly MyNode[] _nodes;

		private static readonly Vector3I[] Directions = new Vector3I[]
		{
			new Vector3I(1, 0, 0),  new Vector3I(-1, 0, 0),
			new Vector3I(0, 1, 0),  new Vector3I(0, -1, 0),
			new Vector3I(0, 0, 1),  new Vector3I(0, 0, -1),
		};

		public Map(Vector3I size)
		{
			_indexer = new Indexer(size);
			int count = _indexer.Count;

			_walkable = new BitField(count, 1);
			_closed = new BitField(count, 1);
			_inOpen = new BitField(count, 1);
			_open = new FastPriorityQueue<MyNode>(count);
			_gScore = new float[count];
			_parent = new int[count];
			_nodes = new MyNode[count];

			for (int i = 0; i < count; i++)
				_nodes[i] = new MyNode { Index = i };
		}

		public void SetWalkable(Vector3I pos, bool walkable)
		{
			_walkable.Set(_indexer.Index(pos), (byte)(walkable ? 1 : 0));
		}

		private void Reset()
		{
			_open.Clear();
			Array.Clear(_gScore, 0, _gScore.Length);
			for (int i = 0; i < _parent.Length; i++) _parent[i] = -1;
			_closed.Clear();
			_inOpen.Clear();
		}

		public List<Vector3I> FindPath(Vector3I start, Vector3I goal)
		{
			int startIndex = _indexer.Index(start.X, start.Y, start.Z);
			int goalIndex = _indexer.Index(goal.X, goal.Y, goal.Z);

			if (startIndex == goalIndex)
			{
				var result = new List<Vector3I>(1);
				result.Add(start);
				return result;
			}

			Reset();

			_gScore[startIndex] = 0f;
			_parent[startIndex] = -1;
			float startH = Manhattan(start, goal);
			_open.Enqueue(_nodes[startIndex], startH);
			_inOpen.Set(startIndex, 1);

			while (_open.Count > 0)
			{
				var current = _open.Dequeue();
				int curIdx = current.Index;

				if (_closed.Get(curIdx) == 1) continue;
				
				_closed.Set(curIdx, 1);

				Vector3I cv;
				_indexer.IndexToPosition(curIdx, out cv);

				if (curIdx == goalIndex)
					return ReconstructPath(goalIndex, goal);

				float curG = _gScore[curIdx];

				for (int d = 0; d < Directions.Length; ++d)
				{
					Vector3I n = cv + Directions[d];

					if (!_indexer.In(n)) continue;

					int nIdx = _indexer.Index(n);

					if (_walkable.Get(nIdx) == 0) continue;

					if (_closed.Get(nIdx) != 0) continue;

					float tentativeG = curG + 1f;

					bool isBetter = _parent[nIdx] == -1 || tentativeG < _gScore[nIdx];

					if (!isBetter) continue;
					
					_gScore[nIdx] = tentativeG;
					_parent[nIdx] = curIdx;

					float h = Manhattan(n, goal);
					if (_inOpen.Get(nIdx) == 1)
						_open.UpdatePriority(_nodes[nIdx], tentativeG + h);
					else
					{
						_open.Enqueue(_nodes[nIdx], tentativeG + h);
						_inOpen.Set(nIdx, 1);
					}
				}
			}

			return null;
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
