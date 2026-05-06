using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageRender;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using VRageMath;

namespace LargeLanguageEngineer
{
	public class FontRenderer
	{
		private readonly FontParser _font;
		private MyStringId[] _atlases;
		private static bool _drawLogDone = false;
		private static bool _uvLogDone = false;

		public FontRenderer(FontParser font) { _font = font; }

		public void LoadAtlases(params string[] atlasPaths)
		{
			_atlases = new MyStringId[atlasPaths.Length];
			for (int i = 0; i < atlasPaths.Length; i++)
				_atlases[i] = MyStringId.GetOrCompute(atlasPaths[i]);
		}

		public void DrawString(string text, Vector2D origin, float scale, Color color)
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null || _atlases == null) return;

			MatrixD camMatrix = camera.WorldMatrix;
			Vector3 left = (Vector3)camMatrix.Left;
			Vector3 up   = (Vector3)camMatrix.Up;

			const double zDistance = 0.5d;

			// ViewProjectionInv — как в BuildInfo/PaintGun DrawUtils
			float aspectRatio = (float)(camera.ViewportSize.X / camera.ViewportSize.Y);
			MatrixD projMatrix = MatrixD.CreatePerspectiveFieldOfView(camera.FovWithZoom, aspectRatio, (float)zDistance, (float)camera.FarPlaneDistance);
			MatrixD viewProjInv = MatrixD.Invert(camera.ViewMatrix * projMatrix);

			// tan(FOV/2) — множитель для конвертации screen→world на расстоянии zDistance
			float scaleFov = (float)Math.Tan(camera.FovWithZoom * 0.5f);

			float cursorX = 0f;
			int drawnCount = 0;

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				FontParser.GlyphInfo glyph;
				if (!_font.Characters.TryGetValue(ch, out glyph)) continue;
				if (glyph.Bm >= _atlases.Length) continue;

				float screenCharWidth  = glyph.Aw * scale;
				float screenCharHeight = (float)glyph.SizeY / glyph.SizeX * glyph.Aw * scale;

				// Размеры в мировых единицах: screenUnits * tan(FOV/2) * distance * aspect(X)
				float charWidth  = screenCharWidth * scaleFov * (float)zDistance * aspectRatio;
				float charHeight = screenCharHeight * scaleFov * (float)zDistance;

				Vector2D charPos = new Vector2D(
					origin.X + cursorX + screenCharWidth / 2,
					origin.Y - screenCharHeight / 2);

				// Screen→World через ViewProjectionInv — как в BuildInfo TextAPIHUDtoWorld
				Vector3D worldPos = ScreenToWorld(charPos, viewProjInv);

				if (ch != ' ')
				{
					DrawGlyph(_atlases[glyph.Bm], glyph, color, worldPos, left, up, charWidth, charHeight);
					drawnCount++;

					// Log first character data for debugging
					if (!_uvLogDone)
					{
						MyLog.Default.WriteLine("LLE DBG: 1st char '" + ch + "' UV=" + glyph.UVOffset.X + "," + glyph.UVOffset.Y + " Size=" + glyph.UVSize.X + "," + glyph.UVSize.Y);
						MyLog.Default.WriteLine("LLE DBG: 1st char WorldPos=" + worldPos.X + "," + worldPos.Y + "," + worldPos.Z + " Dim=" + charWidth + "x" + charHeight);
						_uvLogDone = true;
					}
				}

				cursorX += glyph.Aw * scale;
			}

			if (!_drawLogDone)
			{
				MyLog.Default.WriteLine("LLE DBG: DrawString -> matched=" + drawnCount + " quads added.");
				_drawLogDone = true;
			}
		}

		private void DrawGlyph(MyStringId material, FontParser.GlyphInfo glyph, Color color,
			Vector3D pos, Vector3 left, Vector3 up, float width, float height)
		{
			MyQuadD quad;
			MyUtils.GetBillboardQuadOriented(out quad, ref pos, width / 2f, height / 2f, ref left, ref up);

			var billboard = new MyBillboard();
			billboard.Material                = material;
			billboard.UVOffset                = new Vector2(glyph.UVOffset.X, 1f - glyph.UVOffset.Y - glyph.UVSize.Y);
			billboard.UVSize                  = glyph.UVSize;
			billboard.Position0               = quad.Point0;
			billboard.Position1               = quad.Point1;
			billboard.Position2               = quad.Point2;
			billboard.Position3               = quad.Point3;
			billboard.Color                   = new Vector4(color.R, color.G, color.B, color.A) / 255f;
			billboard.ColorIntensity          = 1f;
			billboard.BlendType               = BlendTypeEnum.PostPP;
			billboard.LocalType               = MyBillboard.LocalTypeEnum.Custom;
			billboard.ParentID                = uint.MaxValue;
			billboard.CustomViewProjection    = -1;
			billboard.Reflectivity            = 0f;
			billboard.SoftParticleDistanceScale = 0f;

			MyTransparentGeometry.AddBillboard(billboard, false);
		}

		// Screen→World через ViewProjectionInv с перспективным делением — как в BuildInfo TextAPIHUDtoWorld
		private static Vector3D ScreenToWorld(Vector2D screenPos, MatrixD viewProjInv)
		{
			double x = screenPos.X * viewProjInv.M11 + screenPos.Y * viewProjInv.M21 + viewProjInv.M41;
			double y = screenPos.X * viewProjInv.M12 + screenPos.Y * viewProjInv.M22 + viewProjInv.M42;
			double z = screenPos.X * viewProjInv.M13 + screenPos.Y * viewProjInv.M23 + viewProjInv.M43;
			double w = screenPos.X * viewProjInv.M14 + screenPos.Y * viewProjInv.M24 + viewProjInv.M44;
			return new Vector3D(x / w, y / w, z / w);
		}
	}
}
