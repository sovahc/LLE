using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
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
			int position = index * _bits;
			int word = position >> 6;
			int shift = position & 63;

			long shifted_mask = ~((long)_mask << shift);
			_data[word] = (_data[word] & shifted_mask) | ((long)(value & _mask) << shift);
		}

		public byte Get(int index)
		{
			int position = index * _bits;
			return (byte)((_data[position >> 6] >> (position & 63)) & _mask);
		}
	}

	public class MapChunk
	{
		public const int Size = 5;
		public const int Volume = Size * Size * Size;

		public const byte Solid = byte.MaxValue;

		private readonly byte[] Field = new byte[Volume];

		private static int Index(int x, int y, int z) =>
			x + (y * Size) + (z * Size * Size);

		public void Set(int x, int y, int z, byte value) =>
			Field[Index(x, y, z)] = value;

		public byte Get(int x, int y, int z) =>
			Field[Index(x, y, z)];
	}

	public class SuperChunk
	{
		public const int Size = MapChunk.Size;
		public const int Volume = Size * Size * Size;

		public const byte Void = 0;
		public const byte HasMap = 1;
		public const byte Solid = 3;

		private readonly BitField _data = new BitField(Volume, 2);
		private readonly MapChunk[] _maps = new MapChunk[Volume];

		private static int Index(int x, int y, int z) =>
			x + (y * Size) + (z * Size * Size);

		// public void Set(int x, int y, int z, byte value)
		//public byte Get(int x, int y, int z)
	}

	class Vision
	{
		private static float FovAngle = (float)Math.PI / 6;
		private static float Tan_HalfFovAngle = (float)Math.Tan(FovAngle/2);
		private static float Cos_HalfFovAngle = (float)Math.Cos(FovAngle/2);

		public static void HighlightVissible(Vector3D botPos, Vector3D botForward, float range = 5000)
		{
			BoundingBoxD searchBox;
			{
				Vector3D center = botPos + botForward * (range / 2);
				float radius = Math.Max(range / 2, range * Tan_HalfFovAngle);

				searchBox = new BoundingBoxD(center - new Vector3(radius), center + new Vector3(radius));
			}

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref searchBox);

			//Log($"{botPos} {botForward} {candidates.Count}");

			foreach (var entity in candidates)
			{
				Vector3D targetPos = entity.PositionComp.WorldMatrixRef.Translation;
				Vector3D dir = targetPos - botPos;

				if (dir.LengthSquared() > range * range) continue;

				double dot = Vector3D.Dot(Vector3D.Normalize(dir), botForward);
				if (dot < Cos_HalfFovAngle) continue;

				Utilities.DrawPoint(targetPos);

				//MyAPIGateway.Physics.CastRay(botPos, targetPos, out var hit);
				//if (hit.HitEntity == null || hit.HitEntity.EntityId == entity.EntityId)
				//{
				//	BotSeesTarget(entity);
				//}
			}
		}
	}

	class Utilities
	{
		private static Color DefaultColor = new Color(255, 255, 127, 255);

		public static void DrawPoint(Vector3D point)
		{	DrawPoint(point, DefaultColor);
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

		public static void Log(string s)
		{
			MyLog.Default.WriteLine("LLE " + s);
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

			var p = player.Character.GetHeadMatrix(false);

			Vision.HighlightVissible(p.Translation, p.Forward);
		}
	}
}
