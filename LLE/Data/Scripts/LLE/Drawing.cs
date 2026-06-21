using System;
using System.Collections.Generic;
using System.Linq;

using VRageMath;
using VRage.Game;
using VRage.Utils;
using VRageRender;
using Sandbox.ModAPI;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace LLE
{
	public class Common
	{
		public static bool Enabled;

		public static float _nearPlane;
		public static MatrixD _viewProj, _viewProjInv;
		public static float _tanHalfFov, _aspectRatio;

		private static readonly ObjectPool<MyBillboard> _pool = new ObjectPool<MyBillboard>();

		public static MyBillboard GetFromPool()
		{	return _pool.Get();
		}

		public static void StartFrame()
		{
			_pool.StartFrame();

			var camera = MyAPIGateway.Session.Camera;
			if (camera == null)
			{	Enabled = false;
				return;
			}
			Enabled = true;

			_nearPlane = camera.NearPlaneDistance;
			_aspectRatio = camera.ViewportSize.X / camera.ViewportSize.Y;
			MatrixD projMatrix = MatrixD.CreatePerspectiveFieldOfView(camera.FovWithZoom, _aspectRatio, _nearPlane, (float)camera.FarPlaneDistance);
			_viewProj = camera.ViewMatrix * projMatrix;
			_viewProjInv = MatrixD.Invert(_viewProj);
			_tanHalfFov = (float)Math.Tan(camera.FovWithZoom * 0.5f);
		}

		private static readonly List<MyBillboard> _billboards = new List<MyBillboard>();

		public static void AddBillboard(MyBillboard b)
		{	_billboards.Add(b);
		}

		public static void Call_Add_Billboards()
		{	if (_billboards.Count == 0) return;
			MyTransparentGeometry.AddBillboards(_billboards, false);
			_billboards.Clear();
		}

		public static MyBillboard Billboard(MyQuadD quad, MyStringId material, Vector4 color)
		{
			var bb = GetFromPool();
			bb.Material = material;
			bb.BlendType = BlendTypeEnum.PostPP;
			bb.Position0 = quad.Point0;
			bb.Position1 = quad.Point1;
			bb.Position2 = quad.Point2;
			bb.Position3 = quad.Point3;
			bb.Color = color;
			bb.ColorIntensity = 1f;
			bb.SoftParticleDistanceScale = 0f;
			bb.UVOffset = Vector2.Zero;
			bb.UVSize = Vector2.One;
			bb.LocalType = MyBillboard.LocalTypeEnum.Custom;
			bb.ParentID = uint.MaxValue;
			bb.DistanceSquared = (float)Vector3D.DistanceSquared(MyAPIGateway.Session.Camera.Position, quad.Point0);
			bb.Reflectivity = 0f;
			bb.AlphaCutout = 0f;
			bb.CustomViewProjection = -1;
			AddBillboard(bb);
			return bb;
		}

		public static Vector3D ScreenToWorld(Vector2D screenPos, MatrixD viewProjInv)
		{
			double x = screenPos.X * viewProjInv.M11 + screenPos.Y * viewProjInv.M21 + viewProjInv.M41;
			double y = screenPos.X * viewProjInv.M12 + screenPos.Y * viewProjInv.M22 + viewProjInv.M42;
			double z = screenPos.X * viewProjInv.M13 + screenPos.Y * viewProjInv.M23 + viewProjInv.M43;
			double w = screenPos.X * viewProjInv.M14 + screenPos.Y * viewProjInv.M24 + viewProjInv.M44;
			return new Vector3D(x / w, y / w, z / w);
		}
	}

	public class Font
	{
		public struct Glyph
		{
			public Vector2 offset;
			public Vector2 size;
			public float aw;
			public int sx;
			public int sy;
		}

		private const int OriginalTextureSize = 1024;

		private Dictionary<char, Glyph> _characters = new Dictionary<char, Glyph>();
		private MyStringId _atlas;

		public float GetHeight(float scale)
		{
			Glyph glyph;
			if (_characters.TryGetValue('H', out glyph))
				return glyph.sx * scale;
			// fallback
			if (_characters.TryGetValue('\u25A1', out glyph))
				return glyph.sx * scale;
			return 0.02f;
		}

		public float String(string text, Vector2D origin, float scale, Color color)
		{
			if(!Common.Enabled) return 0;

			if(text == null) text = "(null)";

			float cursorX = 0f;

			float oX = (float)origin.X;
			float oY = (float)origin.Y;

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				Glyph glyph;
				if (!_characters.TryGetValue(ch, out glyph))
				{
					glyph = _characters['\u25A1']; // Keen unknown character
				}

				float screenCharWidth = glyph.sx * scale;
				float screenCharHeight = glyph.sy * scale;

				Vector2 charTopLeft     = new Vector2(oX + cursorX, oY);
				Vector2 charBottomRight = new Vector2(oX + cursorX + screenCharWidth, oY + screenCharHeight);

				if (ch != ' ')
				{
					var billboard = Rectangle(charTopLeft, charBottomRight,
						_atlas, glyph.offset, glyph.size, color);
					Common.AddBillboard(billboard);
				}

				cursorX += glyph.aw * scale;
			}

			return cursorX;
		}

		public MyBillboard Rectangle(Vector2 topLeft, Vector2 bottomRight, // Screen space -1 to 1
			MyStringId material, Vector2 UVOffset, Vector2 UVSize, Vector4 color)
		{
			if(!Common.Enabled) return null;

			Vector2 center = new Vector2((topLeft.X + bottomRight.X) / 2f, (topLeft.Y + bottomRight.Y) / 2f);
			Vector2 size   = new Vector2(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

			Vector3D worldPos = Common.ScreenToWorld((Vector2D)center, Common._viewProjInv);
			var camera = MyAPIGateway.Session.Camera;
			float halfW = size.X * Common._tanHalfFov * Common._nearPlane * Common._aspectRatio / 2f;
			float halfH = size.Y * Common._tanHalfFov * Common._nearPlane / 2f;
			Vector3 left = (Vector3)camera.WorldMatrix.Left;
			Vector3 up = (Vector3)camera.WorldMatrix.Up;

			MyQuadD quad;
			MyUtils.GetBillboardQuadOriented(out quad, ref worldPos, halfW, halfH, ref left, ref up);

			var billboard = Common.GetFromPool();

			billboard.Material = material;
			billboard.BlendType = BlendTypeEnum.PostPP;
			billboard.Position0 = quad.Point0;
			billboard.Position1 = quad.Point1;
			billboard.Position2 = quad.Point2;
			billboard.Position3 = quad.Point3;
			billboard.Color = color;
			billboard.ColorIntensity = 1f;
			billboard.SoftParticleDistanceScale = 0f;
			billboard.UVOffset = UVOffset;
			billboard.UVSize = UVSize;
			billboard.LocalType = MyBillboard.LocalTypeEnum.Custom;
			billboard.ParentID = uint.MaxValue;
			billboard.DistanceSquared = (float)Vector3D.DistanceSquared(worldPos, camera.Position);
			billboard.Reflectivity = 0f;
			billboard.AlphaCutout = 0f;
			billboard.CustomViewProjection = -1;

			return billboard;
		}

		public bool LoadFont(string xmlPath, string atlas)
		{
			try
			{
				using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(xmlPath))
				{
					byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
					string content = System.Text.Encoding.UTF8.GetString(bytes);
					ParseXml(content.Split('\n'));
				}

				_atlas = MyStringId.GetOrCompute(atlas);

				return true;
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLine("Font parser error: " + e.Message);
				return false;
			}
		}

		private void ParseXml(string[] lines)
		{
			var dict = lines.Where(l => l.Contains("<glyph ") && !l.TrimStart().StartsWith("<!--"))
				.Select(Attrs)
				.Where(a => !a.ContainsKey("bm") || a["bm"] == "0")
				.Where(a => a.ContainsKey("ch") && DecodeChar(a["ch"]) != null)
				.ToDictionary(
						a => (char)DecodeChar(a["ch"]),
						a =>
						{
							var origin = a["origin"].Split(',');
							var sizeParts = a["size"].Split('x');
							int ox = int.Parse(origin[0]);
							int oy = int.Parse(origin[1]);
							int sx = int.Parse(sizeParts[0]);
							int sy = int.Parse(sizeParts[1]);
							return new Glyph
							{
								offset = new Vector2((float)ox / OriginalTextureSize, (float)oy / OriginalTextureSize),
								size = new Vector2((float)sx / OriginalTextureSize, (float)sy / OriginalTextureSize),
								aw = float.Parse(a["aw"]),
								sx = sx,
								sy = sy
							};
						});
			_characters = dict;
		}

		private static Dictionary<string, string> Attrs(string line)
		{
			var d = new Dictionary<string, string>();
			for (int i = 0; ;)
			{
				int start = line.IndexOf('"', i); if (start < 0) break;
				int end = line.IndexOf('"', start + 1); if (end < 0) break;

				int kEnd = start - 1; while (kEnd >= 0 && char.IsWhiteSpace(line[kEnd])) kEnd--;
				int kStart = kEnd;
				while (kStart > 0 && line[kStart - 1] != ' ' && line[kStart - 1] != '>' && line[kStart - 1] != '<') kStart--;

				string key = line.Substring(kStart, kEnd - kStart);
				if (!string.IsNullOrEmpty(key))
				{
					var val = line.Substring(start + 1, end - start - 1)
						.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
					d[key] = val;
				}

				i = end + 1;
			}
			return d;
		}

		private static char? DecodeChar(string text)
		{
			if (text.Length == 1) return text[0];
			int code;
			try
			{
				string num = text.Substring(2);
				if (num.StartsWith("x", StringComparison.OrdinalIgnoreCase))
					code = Convert.ToInt32(num.Substring(1), 16);
				else
					code = Convert.ToInt32(num, 10);
			}
			catch { return null; }
			return (char)code;
		}
	}

	public class Drawing
	{
		public static void Contour(Vector2D[] points, bool closed, float thickness, Vector4 color)
		{
			if(!Common.Enabled) return;
			if (points == null || points.Length < 2) return;

			var camera = MyAPIGateway.Session.Camera;

			var worldPoints = new Vector3D[points.Length];
			for (int i = 0; i < points.Length; i++)
				worldPoints[i] = Common.ScreenToWorld(points[i], Common._viewProjInv);

			int count = closed ? worldPoints.Length : worldPoints.Length - 1;

			var square = MyStringId.GetOrCompute("Square");

			for (int i = 0; i < count; i++)
			{
				Vector3D start = worldPoints[i];
				Vector3D end = worldPoints[(i + 1) % worldPoints.Length];
				if (double.IsNaN(start.X) || double.IsNaN(end.X)) continue;
				var diff = end - start;

				MyPolyLineD polyLine;
				polyLine.LineDirectionNormalized = diff.Normalized();
				polyLine.Point0 = start;
				polyLine.Point1 = end;
				polyLine.Thickness = thickness;

				MyQuadD quad;
				MyUtils.GetPolyLineQuad(out quad, ref polyLine, camera.Position);

				Common.Billboard(quad, square, color);
			}
		}

		private static readonly MyStringId _markerMat = MyStringId.GetOrCompute("LLE-Marker");

		public static void RoundMarker(Vector3D point, Color color)
		{
			if (!Common.Enabled) return;
			var camera = MyAPIGateway.Session.Camera;

			Vector3D viewDir = Vector3D.Normalize(point - camera.Position);
			double distance = (point - camera.Position).Length();

			point = camera.Position + viewDir;

			float size = (float)(0.05 / (distance + 0.0001));
			if (size < 0.005f) size = 0.005f;
			if (size > 0.25f) size = 0.25f;

			Vector3 left = (Vector3)camera.WorldMatrix.Left;
			Vector3 up = (Vector3)camera.WorldMatrix.Up;
			MyQuadD quad;
			MyUtils.GetBillboardQuadOriented(out quad, ref point, size, size, ref left, ref up);

			Common.Billboard(quad, _markerMat, new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
		}

		public static void EllipsoidContour(MatrixD worldMatrix, BoundingBoxD localBB, Color color)
		{
			Vector3D centerLocal = (localBB.Min + localBB.Max) * 0.5;
			Vector3D extents = (localBB.Max - localBB.Min) * 0.5;

			Vector3D localX = new Vector3D(extents.X, 0, 0);
			Vector3D localY = new Vector3D(0, extents.Y, 0);
			Vector3D localZ = new Vector3D(0, 0, extents.Z);

			const int segments = 64;
			Vector2D[] screenPoints = new Vector2D[segments];
			Vector4 c = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
			float thickness = 5e-5f;

			for (int ring = 0; ring < 3; ring++)
			{
				for (int i = 0; i < segments; i++)
				{
					double t = i * MathHelper.TwoPi / segments;
					Vector3D local;
					switch (ring)
					{
						case 0: local = centerLocal + Math.Cos(t) * localX + Math.Sin(t) * localY; break;
						case 1: local = centerLocal + Math.Cos(t) * localX + Math.Sin(t) * localZ; break;
						default: local = centerLocal + Math.Cos(t) * localY + Math.Sin(t) * localZ; break;
					}

					Vector3D world = Vector3D.Transform(local, worldMatrix);
					Vector4D clip = Vector4D.Transform(world, Common._viewProj);
					if (clip.W > 0.001)
					{
						screenPoints[i] = new Vector2D(clip.X / clip.W, clip.Y / clip.W);
					}
					else
					{
						screenPoints[i] = new Vector2D(double.NaN, double.NaN);
					}
				}
				Contour(screenPoints, true, thickness, c);
			}
		}

		public static List<Vector2D> WorldToScreen(List<Vector3D> worldPoints)
		{
			if (!Common.Enabled || worldPoints == null) return new List<Vector2D>();
			var result = new List<Vector2D>();
			for (int i = 0; i < worldPoints.Count; i++)
			{
				Vector4D clip = Vector4D.Transform(worldPoints[i], Common._viewProj);
				if (clip.W > 0.001)
					result.Add(new Vector2D(clip.X / clip.W, clip.Y / clip.W));
			}
			return result;
		}

		public static void ScreenMarkers(Font font, List<Vector2D> screenPoints, Color color)
		{
			if (!Common.Enabled || screenPoints == null || screenPoints.Count == 0) return;

			for (int i = 0; i < screenPoints.Count; i++)
			{
				var sp = screenPoints[i];
				if (double.IsNaN(sp.X)) continue;
				font.String("x", sp, 0.00075f, color);
			}
		}

		public static void ScreenSphere(Vector3D worldCenter, float radius, Vector4 color)
		{
			if(!Common.Enabled) return;
			var camera = MyAPIGateway.Session.Camera;
			Vector3D viewDir = Vector3D.Normalize(worldCenter - camera.Position);

			Vector3D right, localUp;
			Geometry.OrthonormalBasis(viewDir, out right, out localUp);

			int segments = 64;
			var silhouettePoints = new List<Vector3D>();
			for (int i = 0; i < segments; i++)
			{
				double angle = i * MathHelper.TwoPi / segments;
				Vector3D worldPoint = worldCenter + Math.Cos(angle) * right * radius + Math.Sin(angle) * localUp * radius;
				silhouettePoints.Add(worldPoint);
			}
			var projected = WorldToScreen(silhouettePoints);
			Contour(projected.ToArray(), true, 5e-5f, color);
		}
	}
}
