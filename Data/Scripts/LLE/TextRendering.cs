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
	public class TextRendering
	{
		public struct Glyph
		{
			public Vector2 offset;
			public Vector2 size;
			public float aw;
			public int sx;
			public int sy;
		}

		private const int TextureSize = 1024;

		private Dictionary<char, Glyph> _characters = new Dictionary<char, Glyph>();
		private MyStringId _atlas;

		private readonly ObjectPooling<MyBillboard> _pool = new ObjectPooling<MyBillboard>();
		private readonly List<MyBillboard> _billboards = new List<MyBillboard>();

		private const float NearPlaneZ = 0.5f;
		private MatrixD _cachedViewProjInv;
		private float _cachedScaleFov, _cachedAspectRatio;

		public void StartFrame()
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			_pool.StartFrame();

			_cachedAspectRatio = (float)(camera.ViewportSize.X / camera.ViewportSize.Y);
			MatrixD projMatrix = MatrixD.CreatePerspectiveFieldOfView(camera.FovWithZoom, _cachedAspectRatio, NearPlaneZ, (float)camera.FarPlaneDistance);
			_cachedViewProjInv = MatrixD.Invert(camera.ViewMatrix * projMatrix);
			_cachedScaleFov = (float)Math.Tan(camera.FovWithZoom * 0.5f);
		}

		public void DrawString(string text, Vector2D origin, float scale, Color color)
		{
			float cursorX = 0f;

			_billboards.Clear();

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				Glyph glyph;
				if (!_characters.TryGetValue(ch, out glyph)) continue;

				float screenCharWidth = glyph.aw * scale;
				float screenCharHeight = (float)glyph.sy / glyph.sx * glyph.aw * scale;

				Vector2 charCenter = new Vector2(
					(float)(origin.X + cursorX + screenCharWidth / 2),
					(float)(origin.Y - screenCharHeight / 2));

				if (ch != ' ')
				{
					var billboard = DrawRectangle(charCenter,
						new Vector2(screenCharWidth, screenCharHeight),
						_atlas, glyph.offset, glyph.size, color, false);
					_billboards.Add(billboard);
				}

				cursorX += glyph.aw * scale;
			}

			if (_billboards.Count > 0)
			{
				MyTransparentGeometry.AddBillboards(_billboards, false);
			}
		}

		public MyBillboard DrawRectangle(Vector2 xy, Vector2 wh, // Screen space -1 to 1
			MyStringId material, Vector2 UVOffset, Vector2 UVSize, Color color,
			bool callAddBillboard = true)
		{
			var billboard = _pool.Get();
			Vector3D worldPos = ScreenToWorld((Vector2D)xy, _cachedViewProjInv);
			var camera = MyAPIGateway.Session.Camera;
			float halfW = Math.Abs(wh.X * _cachedScaleFov * NearPlaneZ * _cachedAspectRatio / 2f);
			float halfH = Math.Abs(wh.Y * _cachedScaleFov * NearPlaneZ / 2f);
			Vector3 left = (Vector3)camera.WorldMatrix.Left;
			Vector3 up = (Vector3)camera.WorldMatrix.Up;

			MyQuadD quad;
			MyUtils.GetBillboardQuadOriented(out quad, ref worldPos, halfW, halfH, ref left, ref up);

			billboard.Material = material;
			billboard.UVOffset = UVOffset;
			billboard.UVSize = UVSize;
			billboard.Position0 = quad.Point0;
			billboard.Position1 = quad.Point1;
			billboard.Position2 = quad.Point2;
			billboard.Position3 = quad.Point3;
			billboard.Color = new Vector4(color.R, color.G, color.B, color.A) / 255f;
			billboard.ColorIntensity = 1f;
			billboard.BlendType = BlendTypeEnum.PostPP;
			billboard.LocalType = MyBillboard.LocalTypeEnum.Custom;
			billboard.ParentID = uint.MaxValue;
			billboard.CustomViewProjection = -1;
			billboard.Reflectivity = 0f;
			billboard.SoftParticleDistanceScale = 0f;
			billboard.DistanceSquared = (float)Vector3D.DistanceSquared(worldPos, camera.Position);

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


		public bool Parse(string xmlPath)
		{
			try
			{
				using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(xmlPath))
				{
					byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
					string content = System.Text.Encoding.UTF8.GetString(bytes);
					ParseXml(content.Split('\n'));
				}
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
				.ToDictionary(
						a => DecodeChar(a["ch"]),
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
								offset = new Vector2((float)ox / TextureSize, (float)oy / TextureSize),
								size = new Vector2((float)sx / TextureSize, (float)sy / TextureSize),
								aw = float.Parse(a["aw"]),
								sx = sx,
								sy = sy
							};
						});
			_characters = dict;
		}

		public void LoadAtlas(string atlasPath)
		{
			_atlas = MyStringId.GetOrCompute(atlasPath);
		}

		private static char DecodeChar(string text)
		{
			if (text.Length == 1) return text[0];
			throw new Exception("Unknown XML escaped character: " + text);
		}
	}
}
