using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LargeLanguageEngineer
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
	}

	public class MapChunk
	{
		public const int Size = 5;
		public const int Volume = Size * Size * Size;

		public const byte Wall = byte.MaxValue;
		public const byte Space = 0;

		private readonly byte[] Field = new byte[Volume];

		private static int Index(int localX, int localY, int localZ) =>
			localX + (localY * Size) + (localZ * Size * Size);

		public void Set(int x, int y, int z, byte value) =>
			Field[Index(x, y, z)] = value;

		public byte Get(int x, int y, int z) =>
			Field[Index(x, y, z)];
	}

	class Utilities
	{
		public static void Log(string s)
		{
			MyLog.Default.WriteLine("LLE " + s);
		}

		public static void DrawPoint(Vector3D point, Color color)
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			var cameraMatrix = camera.WorldMatrix;

			var material = MyStringId.GetOrCompute("LLE-Marker");

			Vector3D viewDir = Vector3D.Normalize(point - camera.Position);
			var distance = (point - camera.Position).Normalize();

			point = camera.Position + viewDir;

			float size = (float)(0.25 / (distance + 0.0001));
			if (size < 0.001f) size = 0.001f;
			if (size > 0.25f) size = 0.25f;

			MyTransparentGeometry.AddBillboardOriented(material, color, point, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, radius: size);
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		public static void Log(string s) { Utilities.Log(s); }

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var m = player.Character.GetHeadMatrix(false);
			var a = m.Translation;
			var f = m.Forward * 50;

			IHitInfo hitInfo;

			MyAPIGateway.Physics.CastRay(a, a + f, out hitInfo, CollisionLayers.VoxelCollisionLayer);

			if (hitInfo != null)
			{
				var color = new Color(127, 255, 255, 255);
				var intersection = a + f * hitInfo.Fraction;
				Utilities.DrawPoint(intersection, color);
				var size = new Vector3D(1, 1, 1);

				//var material = MyStringId.GetOrCompute("Square");
				//var box = new BoundingBoxD(-size/2, size/2);
				//var wm = MatrixD.CreateTranslation(intersection);
				//var raster = MySimpleObjectRasterizer.Wireframe;
				//MySimpleObjectDraw.DrawTransparentBox(ref wm, ref box, ref color, raster, 1, 0.01f, material, material);
			}
		}
	}
}
