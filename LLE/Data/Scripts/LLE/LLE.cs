using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static void DrawPoint(Vector3D point, Color color)
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			var material = MyStringId.GetOrCompute("LLE-Marker");

			Vector3D viewDir = Vector3D.Normalize(point - camera.Position);
			var distance = (point - camera.Position).Normalize();

			point = camera.Position + viewDir;

			float size = (float)(0.25 / (distance + 0.0001));
			if (size < 0.005f) size = 0.005f;
			if (size > 0.25f) size = 0.25f;

			MyTransparentGeometry.AddBillboardOriented(material, color, point, (Vector3)camera.WorldMatrix.Left, (Vector3)camera.WorldMatrix.Up, radius: size);
		}
	}

	class MyConsole
	{
		struct LineData
		{
			public string Text;
			public Color Color;
		}

		private static readonly List<LineData> _lines = new List<LineData>();
		const int MaxLines = 50;

		private static readonly Color textBackground = new Color(0, 0, 0, 127);

		public static void Add(string text, Color color)
		{
			//Utilities.Log(text);
			_lines.Add(new LineData { Text = text, Color = color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Clear()
		{	_lines.Clear();			
		}

		public static void Render(Drawing draw)
		{
			if (draw == null || _lines.Count == 0) return;

			float B = 0.01f;
			draw.Rectangle(new Vector2(-1+B, B), new Vector2(-0.5f-B, 1f-B),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			float scale = 0.00075f;
			float lineStep = 0.025f;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = B+B + i * lineStep;
				draw.String(line.Text, new Vector2D(-0.99f, y), scale, line.Color);
			}
		}
	}

	class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	class Vision
	{
		private static Random random = new Random();

		internal static readonly Dictionary<long, LastKnownState> lks = new Dictionary<long, LastKnownState>();

		private static void SetLKS(IMyEntity entity, ObjectType type, bool isVisible)
		{
			LastKnownState state;
			if(!lks.TryGetValue(entity.EntityId, out state))
			{
				state = new LastKnownState();

				state.Type = type;
				state.DisplayName = entity.DisplayName;
				var p = entity.GetPosition();
				state.X = p.X;
				state.Y = p.Y;
				state.Z = p.Z;
				state.LastSeenAt = 0;

				lks.Add(entity.EntityId, state);
			}

			if(isVisible)
			{	
				state.DisplayName = entity.DisplayName;
				var p = entity.GetPosition();
				state.X = p.X;
				state.Y = p.Y;
				state.Z = p.Z;
				state.LastSeenAt = Time.Now;
			}
		}

		private static int raycast_slowdown_offset;

		public static void HighlightVisible(Drawing draw, Vector3D rayOrigin, Vector3D rayDir, float range = 1000)
		{
			BoundingSphereD pruneSphere = new BoundingSphereD(rayOrigin, range);

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref pruneSphere);

			const int RAYCAST_EVERY = 10;
			int raycast_slowdown = raycast_slowdown_offset;
			if(++raycast_slowdown_offset >= RAYCAST_EVERY) raycast_slowdown_offset = 0;

			int raycasts = 0;

			foreach (IMyEntity entity in candidates)
			{
				if(entity.Closed) continue;

				Vector3D p;
				IHitInfo hit;

				var grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					bool r = SurfaceSampler.TryGetRandomBlockOnSurface(grid, random, out p);
					if(!r) continue;
					
					MyAPIGateway.Physics.CastRay(rayOrigin, entity.WorldMatrix.Translation, out hit, CollisionLayers.VoxelCollisionLayer);
					++raycasts;
					
					bool isBlocked = hit != null && hit.HitEntity != entity;
					Utilities.DrawPoint(p, isBlocked ? Color.DimGray : Color.LimeGreen);
					if(isBlocked) continue;

					var type = grid.GridSizeEnum == MyCubeSize.Large ? ObjectType.LargeShip : ObjectType.SmallShip;
					SetLKS(entity, type, true);
				}

				var voxel = entity as MyVoxelBase;
				if (voxel != null)
				{
					if (voxel is MyPlanet) continue;
					
					bool r = SurfaceSampler.TryGetRandomSurfacePoint(voxel, random, out p);
					if(!r) continue;
					
					MyAPIGateway.Physics.CastRay(rayOrigin, entity.WorldMatrix.Translation, out hit, CollisionLayers.VoxelCollisionLayer);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;
					Utilities.DrawPoint(p, isBlocked ? Color.DimGray : Color.YellowGreen);
					if(isBlocked) continue;

					SetLKS(entity, ObjectType.Asteroid, true);
				}

				var floater = entity as IMyFloatingObject;
				if(floater != null)
				{
					if(raycast_slowdown++ % 5 != 0) continue;

					MyAPIGateway.Physics.CastRay(rayOrigin, entity.WorldMatrix.Translation, out hit, CollisionLayers.VoxelCollisionLayer);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;

					Utilities.DrawPoint(entity.GetPosition(), isBlocked ? Color.DimGray : Color.Green);
					if(isBlocked) continue;

					SetLKS(entity, ObjectType.Floating, true);
				}
			}

			MyConsole.Clear();
			MyConsole.Add($"raycasts: {raycasts} ", Color.Red);
			foreach(var v in lks.Values)
			{	var delta = Time.Now - v.LastSeenAt;
				if(delta > 1.0) continue;
				double distance = (rayOrigin - new Vector3D(v.X, v.Y, v.Z)).Length();
				MyConsole.Add($"{v.Type} {distance:F1} {delta:F1} {v.DisplayName} ", Color.White);
			}
		}

		public static void OnClose(IMyEntity e)
		{
			var grid = e as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded -= Vision.Grid_OnBlockAdded;
				grid.OnBlockRemoved -= Vision.Grid_OnBlockRemoved;
				grid.OnGridChanged -= Vision.Grid_OnGridChanged;
			}

			lks.Remove(e.EntityId);
		}

		public static void Grid_OnBlockAdded(IMySlimBlock block) { }

		public static void Grid_OnBlockRemoved(IMySlimBlock block) { }

		public static void Grid_OnGridChanged(IMyCubeGrid grid) { }

		public static bool RayIntersectsEllipsoid(Vector3D rayOrigin, Vector3D rayDir, MatrixD worldMatrix, BoundingBoxD localBB)
		{
			var center = (localBB.Min + localBB.Max) * 0.5;
			var radii = (localBB.Max - localBB.Min) * 0.5;

			if (radii.LengthSquared() == 0) return false;

			MatrixD invWorld;
			MatrixD.Invert(ref worldMatrix, out invWorld);

			var localOrigin = Vector3D.Transform(rayOrigin, ref invWorld);
			var localDir = Vector3D.TransformNormal(rayDir, ref invWorld);

			var E = (localOrigin - center) / radii;
			var D = localDir / radii;

			double a = D.Dot(D);
			double b = 2.0 * E.Dot(D);
			double c = E.Dot(E) - 1.0;

			return b * b - 4.0 * a * c >= 0;
		}
	}

	class Navigation
	{
		public bool Active;
		public Vector3D Target;
		const double MaxSpeed = 10.0;
		const float TurnRate = 2.0f;
		const double Decel = 5.0;

		public void Update(IMyCharacter ch)
		{
			if(//MyAPIGateway.Input.IsAnyKeyPress() ||
				MyAPIGateway.Input.IsAnyMousePressed()) { Active = false; return; }

			var pos = ch.GetPosition();
			Vector3D toTarget = Target - pos;
			double dist = toTarget.Length();
			if (dist < 2.0) { Active = false; return; }

			// Speed: accelerate linearly, brake when close
			double brakeDist = MaxSpeed * MaxSpeed / (2.0 * Decel);
			double speed;
			if (dist < brakeDist)
			{
				speed = dist > 0 ? Math.Min(MaxSpeed, dist * Decel / MaxSpeed) : 0;
			}
			else
			{
				speed = MaxSpeed;
			}

			Vector3D targetDir = Vector3D.Normalize(toTarget);

			// Rotation: compute yaw/pitch to face target from current forward
			MatrixD rot = ch.WorldMatrix;
			var fwd = rot.Forward;
			var right = rot.Right;
			var up = rot.Up;
			Vector3 targetDir3 = (Vector3)targetDir;
			float yaw = -(float)Math.Atan2(Vector3.Dot(targetDir3, right), Vector3.Dot(targetDir3, fwd));
			float pitch = (float)Math.Asin(Vector3.Dot(targetDir3, up));

			yaw = Math.Max(-TurnRate, Math.Min(TurnRate, yaw));
			pitch = Math.Max(-TurnRate, Math.Min(TurnRate, pitch));

			MatrixD inv = ch.WorldMatrixInvScaled;
			Vector3 localDir = Vector3.TransformNormal((Vector3)targetDir, inv);
			ch.MoveAndRotate(localDir * (float)speed, new Vector2(pitch, yaw), 0f);
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Drawing draw;
		private static readonly Navigation _navigation = new Navigation();

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			LLE_Loader.Update();

			if (LLE_Loader.IsPresent())
				LLE_Loader.SetVision(Vision.lks);

			// Navigation update
			if (_navigation.Active)
			{
				var ch = MyAPIGateway.Session.Player?.Character;
				if (ch != null) _navigation.Update(ch);
			}

			ServerCommand cmd;
			if (LLE_Loader.GetCommand(out cmd))
			{
				var payload = cmd.Payload.Trim();
				LastKnownState target = null;
				if (TryParseMoveTo(payload, out target))
				{
					_navigation.Target = new Vector3D(target.X, target.Y, target.Z);
					_navigation.Active = true;
					Log("Navigate to " + payload);
				}
				else
				{
					MyVisualScriptLogicProvider.SendChatMessage(payload, "LLM", font: "Blue");
				}
			}
		}


		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			draw = new Drawing();

			if (!draw.LoadFont(@"Fonts\monospace\FontDataPA.xml", "LLE_monospace2048"))
				Log("ERROR: Failed to parse font!");
		}

		public override void Draw()
		{
			draw.StartFrame();

			var lp = LLE_Loader.IsPresent();
			draw.String("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0.5, -0.97), 0.00075f, lp ? Color.White : Color.Red);

			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(draw, p.Translation, p.Forward);

			MyConsole.Render(draw);
		}

		public override void BeforeStart()
		{	
			var entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entities);

			foreach (var e in entities) OnEntityAdd(e);

			MyEntities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered += OnChatMessage;
		}

		protected override void UnloadData()
		{	MyEntities.OnEntityAdd -= OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered -= OnChatMessage;
		}

		void OnEntityAdd(IMyEntity entity)
		{
			entity.OnClose += Vision.OnClose;

			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded += Vision.Grid_OnBlockAdded;
				grid.OnBlockRemoved += Vision.Grid_OnBlockRemoved;
				grid.OnGridChanged += Vision.Grid_OnGridChanged;
			}
		}

		void OnChatMessage(string message, ref bool sendToOthers)
		{	if(!LLE_Loader.IsPresent()) return;
			var player = MyAPIGateway.Session.Player;
			if(player == null) return;
			
			LLE_Loader.SetChat(player.DisplayName, message);
		}

		bool TryParseMoveTo(string payload, out LastKnownState target)
		{
			target = null;
			var upper = payload.ToUpperInvariant();
			if (!upper.StartsWith("MOVE TO ")) return false;

			var searchName = payload.Substring(8).Trim().ToUpperInvariant();
			foreach (var state in Vision.lks.Values)
			{
				if (state.DisplayName != null && state.DisplayName.ToUpperInvariant().Contains(searchName))
				{
					target = state;
					
					Log($"MOVE TO: {target.Type} {target.DisplayName}");
					return true;
				}
			}
			Log("MOVE TO: target not found: " + searchName);
			return false;
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static void Update() { }
		public static void SetVision(Dictionary<long, LastKnownState> states) { }
		public static void SetChat(string author, string text) { }
		public static bool GetCommand(out ServerCommand cmd) { cmd = null; return false; }
	}
}
