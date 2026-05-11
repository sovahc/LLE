using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageRender;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using VRageMath;

namespace LLE
{
	public class Drawing
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

		private readonly ObjectPooling<MyBillboard> _pool = new ObjectPooling<MyBillboard>();
		private readonly List<MyBillboard> _billboards = new List<MyBillboard>();

		private float _cachedNearPlane;
		private MatrixD _cachedViewProjInv;
		private MatrixD _cachedViewProj;
		private float _cachedScaleFov, _cachedAspectRatio;

		private bool Enabled;

		public void StartFrame()
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null)
			{	Enabled = false;
				return;
			}
			Enabled = true;

			_pool.StartFrame();

			_cachedNearPlane = camera.NearPlaneDistance;
			_cachedAspectRatio = (float)(camera.ViewportSize.X / camera.ViewportSize.Y);
			MatrixD projMatrix = MatrixD.CreatePerspectiveFieldOfView(camera.FovWithZoom, _cachedAspectRatio, _cachedNearPlane, (float)camera.FarPlaneDistance);
			_cachedViewProj = camera.ViewMatrix * projMatrix;
			_cachedViewProjInv = MatrixD.Invert(_cachedViewProj);
			_cachedScaleFov = (float)Math.Tan(camera.FovWithZoom * 0.5f);
		}

		public void String(string text, Vector2D origin, float scale, Color color)
		{
			if(!Enabled) return;

			float cursorX = 0f;

			_billboards.Clear();

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				Glyph glyph;
				if (!_characters.TryGetValue(ch, out glyph))
				{
					glyph = _characters['\u25A1']; // Keen unknown character
				}

				float screenCharWidth = glyph.aw * scale;
				float screenCharHeight = (float)glyph.sy / glyph.sx * glyph.aw * scale;

				Vector2 charTopLeft     = new Vector2((float)origin.X + cursorX, (float)origin.Y);
				Vector2 charBottomRight = new Vector2((float)origin.X + cursorX + screenCharWidth, (float)origin.Y - screenCharHeight);

				if (ch != ' ')
				{
					var billboard = Rectangle(charTopLeft, charBottomRight,
						_atlas, glyph.offset, glyph.size, color, false);
					_billboards.Add(billboard);
				}

				cursorX += glyph.aw * scale;
			}

			if (_billboards.Count > 0) MyTransparentGeometry.AddBillboards(_billboards, false);
		}
		
		public void Contour(Vector2D[] points, bool closed, float thickness, Vector4 color)
		{
			if(!Enabled) return;
			if (points == null || points.Length < 2) return;
			
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			var worldPoints = new Vector3D[points.Length];
			for (int i = 0; i < points.Length; i++)
				worldPoints[i] = ScreenToWorld(points[i], _cachedViewProjInv);

			_billboards.Clear();
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

				var billboard = _pool.Get();

				billboard.Material = square;
				billboard.BlendType = BlendTypeEnum.PostPP;
				billboard.Position0 = quad.Point0;
				billboard.Position1 = quad.Point1;
				billboard.Position2 = quad.Point2;
				billboard.Position3 = quad.Point3;
				billboard.Color = color;
				billboard.ColorIntensity = 1f;
				billboard.SoftParticleDistanceScale = 0f;
				billboard.UVOffset = Vector2.Zero;
				billboard.UVSize = Vector2.One;
				billboard.LocalType = MyBillboard.LocalTypeEnum.Custom;
				billboard.ParentID = uint.MaxValue;
				billboard.DistanceSquared = (float)Vector3D.DistanceSquared(camera.Position, start);
				billboard.Reflectivity = 0f;
				billboard.AlphaCutout = 0f;
				billboard.CustomViewProjection = -1;

				_billboards.Add(billboard);
			}

			if (_billboards.Count > 0) MyTransparentGeometry.AddBillboards(_billboards, false);
		}

		public MyBillboard Rectangle(Vector2 topLeft, Vector2 bottomRight, // Screen space -1 to 1
			MyStringId material, Vector2 UVOffset, Vector2 UVSize, Vector4 color,
			bool callAddBillboard = true)
		{
			if(!Enabled) return null;

			Vector2 center = new Vector2((topLeft.X + bottomRight.X) / 2f, (topLeft.Y + bottomRight.Y) / 2f);
			Vector2 size   = new Vector2(bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);

			Vector3D worldPos = ScreenToWorld((Vector2D)center, _cachedViewProjInv);
			var camera = MyAPIGateway.Session.Camera;
			float halfW = Math.Abs(size.X * _cachedScaleFov * _cachedNearPlane * _cachedAspectRatio / 2f);
			float halfH = Math.Abs(size.Y * _cachedScaleFov * _cachedNearPlane / 2f);
			Vector3 left = (Vector3)camera.WorldMatrix.Left;
			Vector3 up = (Vector3)camera.WorldMatrix.Up;

			MyQuadD quad;
			MyUtils.GetBillboardQuadOriented(out quad, ref worldPos, halfW, halfH, ref left, ref up);

			var billboard = _pool.Get();

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

			if(callAddBillboard)
				MyTransparentGeometry.AddBillboard(billboard, false);

			return billboard;
		}

		private static Vector3D ScreenToWorld(Vector2D screenPos, MatrixD viewProjInv)
		{
			double x = screenPos.X * viewProjInv.M11 + screenPos.Y * viewProjInv.M21 + viewProjInv.M41;
			double y = screenPos.X * viewProjInv.M12 + screenPos.Y * viewProjInv.M22 + viewProjInv.M42;
			double z = screenPos.X * viewProjInv.M13 + screenPos.Y * viewProjInv.M23 + viewProjInv.M43;
			double w = screenPos.X * viewProjInv.M14 + screenPos.Y * viewProjInv.M24 + viewProjInv.M44;
			return new Vector3D(x / w, y / w, z / w);
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

		public static void AABB(MatrixD worldMatrix, BoundingBox localBB, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.002f)
		{
			AABB(worldMatrix, new BoundingBoxD(localBB.Min, localBB.Max), color, raster, thickness);
		}

		public static void AABB(MatrixD worldMatrix, BoundingBoxD localBB, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.002f)
		{
			var material = MyStringId.GetOrCompute("Square");
			Vector3D centerLocal = (localBB.Min + localBB.Max) * 0.5;
			Vector3D extentsLocal = (localBB.Max - localBB.Min) * 0.5;
			var worldCenter = Vector3D.Transform(centerLocal, ref worldMatrix);
			
			MatrixD drawMatrix = MatrixD.CreateFromQuaternion(QuaternionD.CreateFromRotationMatrix(worldMatrix));
			drawMatrix.Translation = worldCenter;
			
			var bbD = new BoundingBoxD(-extentsLocal, extentsLocal);
			MySimpleObjectDraw.DrawTransparentBox(ref drawMatrix, ref bbD, ref color, raster, 1, thickness, material, material);
		}

		public void EllipsoidContour(MatrixD worldMatrix, BoundingBoxD localBB, Color color)
		{
			if (!Enabled) return;

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
					Vector4D clip = Vector4D.Transform(world, _cachedViewProj);
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
	}
}
