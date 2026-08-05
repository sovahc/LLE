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
		public Vector3D Center; // world position the label points at
		public string Text;     // "I J K"
	}

	// The scan runs in UpdateBeforeSimulation and the labels are drawn in Draw(), so the list
	// lives here, between the two. The coroutine only fills it and flips Visible.
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

				// Marker, leader and text together: the marker states which point the digits
				// belong to, which is what the model gets wrong when the label just floats.
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

		// The visibility ray is aimed past the face so that a hit on the face itself is found
		// rather than missed; one large cell is plenty.
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

				// Draw() runs after this Update(), so the overlay needs a frame of its own
				// before the renderer is asked for a copy of the backbuffer.
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
				// Also runs when the stack is disposed by AbortCommand — the overlay must never
				// be left on.
				ScreenshotOverlay.Visible = false;
				MyConsole.Visible = consoleWasVisible;
			}
		}

		// One frame, no yields. The command is issued from a standstill and a hitch just before
		// the shutter costs nothing; spreading the scan over frames would instead let the camera
		// move away from the labels computed on the first one.
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

				// One cell step along each grid axis, in world space. Constant per grid, and it
				// spares the code any assumption about where I, J and K point in the world.
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

		// A block counts as visible when nothing stands between the camera and one of its open
		// faces. Two things matter here and both were learned the hard way.
		// The aim point is the face, not the centre: the centre of a floor tile twenty metres
		// away lies below the tiles in front of it and no ray ever gets there, so a floor
		// filling the screen came back invisible.
		// And the question is distance, not identity: a slope's face centre is empty space and
		// the ray flies straight through it, a grazing ray clips the neighbour a few
		// centimetres early — both are blocks in plain sight that an identity test discards.
		private bool TryFindVisibleFace(IMyCubeGrid grid, IMySlimBlock block, Vector3D[] axes, Vector3D eye, out Vector3D face)
		{
			face = Vector3D.Zero;

			var min = block.Min;
			var max = block.Max;
			var mid = new Vector3I((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);

			// How much closer than the face a hit has to be to count as standing in the way.
			// A ray grazing along a wall clips the neighbour a few centimetres early.
			double slack = grid.GridSize * 0.1;

			foreach (var d in Constants.SixDirections)
			{
				// Middle cell of this face of the block, and the cell right outside it.
				var faceCell =
					d.X != 0 ? new Vector3I(d.X > 0 ? max.X : min.X, mid.Y, mid.Z) :
					d.Y != 0 ? new Vector3I(mid.X, d.Y > 0 ? max.Y : min.Y, mid.Z) :
					           new Vector3I(mid.X, mid.Y, d.Z > 0 ? max.Z : min.Z);

				if (grid.GetCubeBlock(faceCell + d) != null) continue; // covered by a neighbour

				var outward = axes[0] * d.X + axes[1] * d.Y + axes[2] * d.Z;
				var point = grid.GridIntegerToWorld(faceCell) + outward * 0.5;

				var ray = point - eye;
				if (Vector3D.Dot(outward, ray) >= 0) continue; // face turned away from the camera

				double length = ray.Length();
				if (length < 0.01) continue;
				ray /= length;

				IHitInfo hit;
				MyAPIGateway.Physics.CastRay(eye, point + ray * RayOvershoot, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

				// Nothing at all, the block itself, or something behind it — in every one of
				// those the way to the face is clear. Only a hit in front of the face hides it.
				if (hit != null && Vector3D.Distance(eye, hit.Position) < length - slack) continue;

				face = point;
				return true;
			}

			return false;
		}
	}
}
