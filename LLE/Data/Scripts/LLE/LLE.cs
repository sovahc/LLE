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

		public static void DebugRaycast(Vector3D origin, Vector3D direction, float range = 1000)
		{
			IHitInfo hit = null;
			MyAPIGateway.Physics.CastRay(origin, origin + direction * range, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hit == null) return;

			DrawPoint(hit.Position, Color.Red);

			var grid = hit.HitEntity.GetTopMostParent() as IMyCubeGrid;
			if (grid == null) return;

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(origin, origin + direction * range);
			grid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null) return;
			
			if (slimBlock.FatBlock != null)
				Drawing.AABB(slimBlock.FatBlock.WorldMatrix, slimBlock.FatBlock.LocalAABB, Color.YellowGreen);
			else
			{
				double blockSize = grid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 0.5;
				blockSize *= 1.1;
				Vector3D center = grid.GridIntegerToWorld(slimBlock.Position);

				MatrixD matrix = grid.WorldMatrix;
				matrix.Translation = center; // сдвигаем матрицу грида в центр блока

				var v = new Vector3D(blockSize * 0.5);
				Drawing.AABB(matrix, new BoundingBoxD(-v, v), Color.Beige);
			}
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
			float scale = 0.00075f;
			//float lineStep = 0.025f;
			float lineStep = draw.GetFontHeight(scale) * 1.2f;

			float y0 = 0;
			float x0 = -0.99f;
			float rectangleH = _lines.Count * lineStep;
			float rectangleW = 0;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = y0 + i * lineStep;
				var w = draw.String(line.Text, new Vector2D(x0, y), scale, line.Color);
				if(w > rectangleW) rectangleW = w;
			}

			draw.Rectangle(new Vector2(x0-B, y0-B), new Vector2(x0+rectangleW+B+B, y0+rectangleH+B+B),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);
		}
	}

	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

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
				state.EntityId = entity.EntityId;
				state.DisplayName = entity.DisplayName;
				var p = entity.GetPosition();
				state.X = p.X;
				state.Y = p.Y;
				state.Z = p.Z;
				state.LastSeenAt = 0;
				state.debug = "";

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
					if(raycast_slowdown++ % RAYCAST_EVERY != 0) continue;

					MyAPIGateway.Physics.CastRay(rayOrigin, entity.WorldMatrix.Translation, out hit, CollisionLayers.VoxelCollisionLayer);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;

					Utilities.DrawPoint(entity.GetPosition(), isBlocked ? Color.DimGray : Color.Green);
					if(isBlocked) continue;

					SetLKS(entity, ObjectType.Floating, true);
				}
			}

			MyConsole.Add($"raycasts: {raycasts} ", Color.Red);
			foreach(var v in lks.Values)
			{	var delta = Time.Now - v.LastSeenAt;
				v.Visible = delta < 1.0;
				if(!v.Visible) continue;

				double distance = (rayOrigin - new Vector3D(v.X, v.Y, v.Z)).Length();
				MyConsole.Add($"{v.Type} {distance:F0} {delta:F0} {v.DisplayName} {v.debug}", Color.White);
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
		private const float LOOKAHEAD_TIME = 3.0f;
		private const float SAFETY_MARGIN = 5.0f;
		private const float AVOIDANCE_STRENGTH = 10.0f;

		public void ObstacleAvoidance(IMyCharacter ch)
		{
			Vector3D botPos = ch.GetPosition();
			Vector3D botVel = ch.Physics.LinearVelocity;
			double botRadius = 1.0;

			Vector3D totalAvoidance = Vector3D.Zero;
			int dangerCount = 0;

			foreach (var state in Vision.lks.Values)
			{
				if (!state.Visible) continue;

				var entity = MyAPIGateway.Entities.GetEntityById(state.EntityId);
				if (entity == null || entity.Closed) continue;

				double obsRadius = entity.WorldVolume.Radius;
				Vector3D obsPosition = entity.WorldVolume.Center;
				Vector3D obsVelocity = Vector3D.Zero;
				if (entity.Physics != null) obsVelocity = entity.Physics.LinearVelocity;

	            Vector3D relPos = obsPosition - botPos;
            	Vector3D relVel = obsVelocity - botVel;

            	double relVelSq = relVel.LengthSquared();
            	if (relVelSq < 0.01) continue;

            	// 2. Время максимального сближения (Time of Closest Approach)
            	// Минимизируем |relPos + relVel * t|^2 -> производная = 0
            	double t_ca = -Vector3D.Dot(relPos, relVel) / relVelSq;

            	// Нас интересует только будущее в пределах окна предсказания
            	if (t_ca < 0) t_ca = 0;
            	if (t_ca > LOOKAHEAD_TIME) t_ca = LOOKAHEAD_TIME;

            	// 3. Позиции в момент максимального сближения
            	Vector3D botAtCa = botPos + botVel * t_ca;
            	Vector3D obsAtCa = obsPosition + obsVelocity * t_ca;

            	Vector3D distVec = botAtCa - obsAtCa;
            	double distSq = distVec.LengthSquared();

            	double combinedRadius = botRadius + obsRadius + SAFETY_MARGIN;
            	double combinedRadiusSq = combinedRadius * combinedRadius;

            	if (distSq < combinedRadiusSq)
            	{
                	Vector3D avoidDir = Vector3D.Normalize(distVec);

                	double distFactor = 1.0 - (Math.Sqrt(distSq) / combinedRadius); // 1 при касании, 0 на границе
                	double timeFactor = 1.0 - (t_ca / LOOKAHEAD_TIME);              // 1 если сейчас, 0 если через 3 сек
                	double urgency = Math.Max(0, distFactor * timeFactor);

					MyConsole.Add($"distFactor {distFactor:F2} timeFactor{timeFactor:F2} urgency {urgency:F2}", Color.Gray);

                	totalAvoidance += avoidDir * AVOIDANCE_STRENGTH * urgency;
                	dangerCount++;
            	}
			}

			if (dangerCount == 0) return;

			MyConsole.Add($"dangerCount {dangerCount} totalAvoidance {Vector3D.Normalize(totalAvoidance)}", Color.Green);
			//Vector3 localDir = Vector3.TransformNormal((Vector3)moveDir, ch.WorldMatrixInvScaled);
			ch.MoveAndRotate(Vector3.Up, Vector2.Zero, 0f);
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
			MyConsole.Clear();

			//

			LLE_Loader.Update();

			if (LLE_Loader.IsPresent())
				LLE_Loader.SetVision(Vision.lks);

			ServerCommand cmd;
			if (LLE_Loader.GetCommand(out cmd))
			{
				var payload = cmd.Payload.Trim();
				LastKnownState target = null;
				if (TryParseMoveTo(payload, out target))
				{
					//_navigation.Target = new Vector3D(target.X, target.Y, target.Z);
					//_navigation.Active = true;
					Log("Navigate to " + payload);
				}
				else
				{
					MyVisualScriptLogicProvider.SendChatMessage(payload, "LLM", font: "Blue");
				}
			}

			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if(ch == null) return;

			//if (!MyAPIGateway.Input.IsAnyMousePressed())
			//double t = Time.Now;
			//Vector3 dir = new Vector3((float)Math.Sin(t * Math.PI * 2 / 10), (float)Math.Cos(t * Math.PI * 2 / 10), 0);
			//ch.MoveAndRotate(dir, Vector2.Zero, 0f);
			_navigation.ObstacleAvoidance(ch);
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

			// Debug raycast
			Utilities.DebugRaycast(p.Translation, p.Forward);

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
