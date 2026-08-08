using System;
using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.ModAPI;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	struct LabelledBlock
	{
		public Vector3D Center;
		public string Text;
	}

	static class ScreenshotOverlay
	{
		public static bool Visible;
		public static readonly List<LabelledBlock> Blocks = new List<LabelledBlock>();

		private const float TextScale = 7e-4f;
		private const float LeaderThickness = 5e-5f;
		private static readonly Vector2D LabelOffset = new Vector2D(0.02, 0.02);
		private static readonly Color LabelColor = Color.Magenta;

		private static readonly List<Vector2D> leader = new List<Vector2D> { Vector2D.Zero, Vector2D.Zero };

		public static void Render(Font font)
		{
			if (!Visible) return;

			for (int i = 0; i < Blocks.Count; ++i)
			{
				var b = Blocks[i];

				Vector2D anchor;
				if (!Drawing.WorldToScreen(b.Center, out anchor)) continue;

				var text = anchor + LabelOffset;

				Drawing.RoundMarker(b.Center, LabelColor);

				leader[0] = anchor;
				leader[1] = text;
				Drawing.Contour(leader, false, LeaderThickness, LabelColor.ToVector4());

				font.String(b.Text, text, TextScale, LabelColor);
			}
		}
	}

	public partial class Commands
	{
		private const double ScreenshotBlockDistance = 25;
		private const double ScreenshotTimeout = 10;

		// Aimed past the face, so a hit on the face itself is found rather than missed.
		private const double RayOvershoot = 2.5;

		internal IEnumerator Screenshot()
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) yield return "Internal error: MyAPIGateway.Session.Camera is null";

			ScreenshotOverlay.Blocks.Clear();
			CollectLabelledBlocks(camera, ScreenshotOverlay.Blocks);

			bool consoleWasVisible = MyConsole.Visible;

			try
			{
				// The console is billboards like everything else and would end up in the frame.
				MyConsole.Visible = false;
				ScreenshotOverlay.Visible = true;

				// Draw() runs after this Update(), so the overlay needs a frame of its own first.
				yield return null;
				yield return null;

				LLE_Loader.RequestScreenshot();

				double deadline = Time.Now + ScreenshotTimeout;
				bool success;
				while (!LLE_Loader.ScreenshotDone(out success))
				{
					if (Time.Now > deadline)
						yield return "Error: the renderer did not produce a screenshot in time.";
					yield return null;
				}

				if (!success) yield return "Error: the renderer failed to save the screenshot.";

				yield return Success($"Screenshot taken, {ScreenshotOverlay.Blocks.Count} block(s) labelled with their I J K.");
			}
			finally
			{
				// Also runs when AbortCommand disposes the stack; the overlay must never stay on.
				ScreenshotOverlay.Visible = false;
				MyConsole.Visible = consoleWasVisible;
			}
		}

		private void CollectLabelledBlocks(IMyCamera camera, List<LabelledBlock> result)
		{
			var eye = camera.Position;

			var sphere = new BoundingSphereD(eye, Constants.NearInformationRadius);
			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

			var blocks = new List<IMySlimBlock>();
			var axes = new Vector3D[3];

			foreach (var e in entities)
			{
				if (e.Closed) continue;

				var grid = e as IMyCubeGrid;
				if (grid == null) continue;
				if (grid.DisplayName == DraftGridName) continue;

				var box = grid.PositionComp.WorldAABB;
				if (!camera.IsInFrustum(ref box)) continue;

				var origin = grid.GridIntegerToWorld(Vector3I.Zero);
				axes[0] = grid.GridIntegerToWorld(new Vector3I(1, 0, 0)) - origin;
				axes[1] = grid.GridIntegerToWorld(new Vector3I(0, 1, 0)) - origin;
				axes[2] = grid.GridIntegerToWorld(new Vector3I(0, 0, 1)) - origin;

				blocks.Clear();
				grid.GetBlocks(blocks);

				foreach (var block in blocks)
				{
					var center = (grid.GridIntegerToWorld(block.Min) + grid.GridIntegerToWorld(block.Max)) * 0.5;

					if (Vector3D.DistanceSquared(eye, center) > ScreenshotBlockDistance * ScreenshotBlockDistance)
						continue;

					Vector2D screen;
					if (!Drawing.WorldToScreen(center, out screen)) continue;
					if (screen.X < -1 || screen.X > 1 || screen.Y < -1 || screen.Y > 1) continue;

					Vector3D face;
					if (!TryFindVisibleFace(grid, block, axes, eye, out face)) continue;

					result.Add(new LabelledBlock { Center = face, Text = IJK(block.Min) });
				}
			}
		}

		// Aim at the face, not the centre: a floor tile's centre lies below the tiles in front
		// of it and no ray reaches it. And test distance, not identity: a slope's face centre is
		// empty space, and a grazing ray clips the neighbour a few centimetres early.
		private bool TryFindVisibleFace(IMyCubeGrid grid, IMySlimBlock block, Vector3D[] axes, Vector3D eye, out Vector3D face)
		{
			face = Vector3D.Zero;

			var min = block.Min;
			var max = block.Max;
			var mid = new Vector3I((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);

			// How much closer than the face a hit must be to count as standing in the way.
			double slack = grid.GridSize * 0.1;

			foreach (var d in Constants.SixDirections)
			{
				var faceCell =
					d.X != 0 ? new Vector3I(d.X > 0 ? max.X : min.X, mid.Y, mid.Z) :
					d.Y != 0 ? new Vector3I(mid.X, d.Y > 0 ? max.Y : min.Y, mid.Z) :
					           new Vector3I(mid.X, mid.Y, d.Z > 0 ? max.Z : min.Z);

				if (grid.GetCubeBlock(faceCell + d) != null) continue;

				var outward = axes[0] * d.X + axes[1] * d.Y + axes[2] * d.Z;
				var point = grid.GridIntegerToWorld(faceCell) + outward * 0.5;

				var ray = point - eye;
				if (Vector3D.Dot(outward, ray) >= 0) continue;

				double length = ray.Length();
				if (length < 0.01) continue;
				ray /= length;

				IHitInfo hit;
				MyAPIGateway.Physics.CastRay(eye, point + ray * RayOvershoot, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

				if (hit != null && Vector3D.Distance(eye, hit.Position) < length - slack) continue;

				face = point;
				return true;
			}

			return false;
		}
	}
}
