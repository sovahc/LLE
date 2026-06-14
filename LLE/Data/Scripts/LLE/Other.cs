using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	class Constants
	{
		public const float EngineerCapsuleHeight = 0.8f;
		public const float EngineerCapsuleRadius = 0.5f;
		public const float EngineerHeight = 1.8f;

		public const float WeldAndGrindSpeed = 0.03f;
		public const float SoundVolume = 0.5f;

		public static readonly Vector3I[] SixDirections = new Vector3I[]
		{	
			new Vector3I(-1, 0, 0), new Vector3I(1, 0, 0),
			new Vector3I(0, -1, 0), new Vector3I(0, 1, 0),
			new Vector3I(0, 0, -1), new Vector3I(0, 0, 1)
		};
	}

	static class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static int IndexOf(this StringBuilder sb, char c, int startIndex = 0)
		{
			for (int i = startIndex; i < sb.Length; ++i)
			{
				if(sb[i] == c) return i;
			}
			return -1;
		}

		public static void MyRaycast(Vector3D origin, Vector3D direction,
			out IMyCubeGrid grid, out Vector3I position, out Vector3I freeSpace, float range = 500)
		{
			grid = null;
			position = Vector3I.Zero;
			freeSpace = Vector3I.Zero;

			IHitInfo hit;
			MyAPIGateway.Physics.CastRay(origin, origin + direction * range, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hit == null) return;

			var g = hit.HitEntity.GetTopMostParent() as IMyCubeGrid;
			if (g == null) return;

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(origin, origin + direction * range);
			g.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null) return;

			grid = g;
			position = grid.WorldToGridInteger(origin + direction * dist);

			freeSpace = grid.WorldToGridInteger(origin + direction * (dist - grid.GridSize));
		}

		public static void HighlightCell(IMyCubeGrid grid, Vector3I position, Color color)
		{
			float blockSize = MyDefinitionManager.Static.GetCubeSize(grid.GridSizeEnum);

			Vector3D world = grid.GridIntegerToWorld(position);

			MatrixD matrix = grid.WorldMatrix;
			matrix.Translation = world;

			var v = new Vector3D(blockSize * 0.5);

			var bb = new BoundingBoxD(-v, v);

			var material = MyStringId.GetOrCompute("Square");
			MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
				MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
		}

		public static void Tick(ref IEnumerator iter, string name)
		{
			if (iter == null) return;
			var start = Stopwatch.GetTimestamp();
			var limit = start + TimeSpan.TicksPerMillisecond / 2;
			long now = start;
			for (int i = 0; i < 100; ++i)
			{
				if (!iter.MoveNext())
				{
					(iter as IDisposable)?.Dispose();
					iter = null;
					break;
				}
				now = Stopwatch.GetTimestamp();
				if (now >= limit) break;
			}
			var ms = (now - start) / (double)TimeSpan.TicksPerMillisecond;
			MyConsole.Add($"{name}: {ms:0.##}", Color.IndianRed);
		}

		public static Vector3D GetEngineerCenter(IMyCharacter ch)
		{
			return ch.GetPosition() + Constants.EngineerHeight/2 * ch.WorldMatrix.Up;
		}
	}

	/// <inheritdoc cref="Profiler" />
	/// <summary>
	/// This code was provided by Digi as a simple profiler
	/// Usage:
	///		Wrap code you want to profile in:
	///			using(new Profiler("somename"))
	///			{
	///				// code to profile
	///			}
	/// </summary>
	public struct Profiler : IDisposable
	{
		private readonly string _name;
		private readonly long _start;

		public Profiler(string name = "unnamed")
		{
			_name = name;
			_start = Stopwatch.GetTimestamp();
		}

		public override string ToString()
		{
			long end = Stopwatch.GetTimestamp();
			TimeSpan timespan = new TimeSpan(end - _start);
			return $"{_name} {timespan.TotalMilliseconds:0.###}ms";
		}

		public void Dispose() { }
	}

	public class MyMarkdown
	{
		private static readonly Dictionary<string, StringBuilder> data = new Dictionary<string, StringBuilder>();

		private static readonly StringBuilder result = new StringBuilder();

		public static void Clear()
		{	
			result.Clear();
			foreach(var b in data.Values) b.Clear();
		}

		public static void Append(string s)
		{	
			result.Append(s);
			result.Append('\n');
		}

		public static void Add(string category, string element)
		{
			StringBuilder b;

			if(data.TryGetValue(category, out b))
			{	b.Append(element);
				b.Append('\n');
				return;
			}
			
			b = new StringBuilder();
			b.Append(element);
			b.Append('\n');
			data[category] = b;
		}

		public static string Result()
		{
			foreach(var kv in data)
			{	
				if(kv.Value.Length > 0)
				{	result.Append(kv.Key);
					result.Append('\n');
					result.Append(kv.Value);
				}
			}
			return result.ToString();
		}
	}

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

	class TokenParser
	{
		private readonly string s;
		private int index, nextIndex;
		private int tStart, tLength;

		public TokenParser(string input) { s = input; index = 0; }

		private void SkipSpaces()
		{
			while (index < s.Length && s[index] == ' ') ++index;
		}

		public void Peek()
		{
			SkipSpaces();
			nextIndex = index;
			tStart = index;
			tLength = 0;

			if (index >= s.Length) return;

			int i = index;

			if (s[i] == '"' || s[i] == '\'')
			{
				char quote = s[i];
				++i;
				tStart = i;
				while (i < s.Length && s[i] != quote) ++i;
				tLength = i - tStart;
				if (i < s.Length) ++i;
			}
			else
			{
				while (i < s.Length && s[i] != ' ') ++i;
				tLength = i - tStart;
			}

			nextIndex = i;
		}

		public bool Match(string expected, bool ignoreCase = true)
		{
			Peek();

			var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			if (tLength == expected.Length && string.Compare(s, tStart, expected, 0, tLength, cmp) == 0)
			{
				index = nextIndex;
				return true;
			}
			return false;
		}

		public string NextString()
		{
			Peek();
			string result = s.Substring(tStart, tLength);
			index = nextIndex;
			return result;
		}

		public bool NextInt(out int value)
		{
			Peek();
			if (int.TryParse(s.Substring(tStart, tLength), out value))
			{	index = nextIndex;
				return true;
			}
			value = 0;
			return false;
		}

		public bool NextDouble(out double value)
		{
			Peek();
			if (double.TryParse(s.Substring(tStart, tLength), out value))
			{	index = nextIndex;
				return true;
			}
			value = 0;
			return false;
		}

		public bool NextVector3I(out Vector3I value)
		{
			int x, y, z;
			if (NextInt(out x) && NextInt(out y) && NextInt(out z))
			{	value = new Vector3I(x, y, z);
				return true;
			}
			value = Vector3I.Zero;
			return false;
		}

		public bool End
		{
			get { SkipSpaces(); return index >= s.Length; }
		}
	}
}
