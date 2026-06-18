using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	// Deltas: NEW, GONE, MOVED, grouping

    class Vision
	{
		private static Random random = new Random();
		private const int RAYCAST_SKIP_INTERVAL = 10;
		private static int _frameSkipOffset;

		internal static readonly Dictionary<long, LastKnownState> lks = new Dictionary<long, LastKnownState>();

		private static void SetLKS(IMyEntity entity, ObjectType type, bool isVisible)
		{
			LastKnownState state;
			if (!lks.TryGetValue(entity.EntityId, out state))
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
				state.Debug = "";

				lks.Add(entity.EntityId, state);
			}

			if (isVisible)
			{
				state.DisplayName = entity.DisplayName;
				var p = entity.GetPosition();
				state.X = p.X;
				state.Y = p.Y;
				state.Z = p.Z;
				state.LastSeenAt = Time.Now;
			}
		}

		public static void Tick(Vector3D engineer, float range = 1000)
		{
			BoundingSphereD pruneSphere = new BoundingSphereD(engineer, range);

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref pruneSphere);

			int localSkipCounter = _frameSkipOffset;
			_frameSkipOffset = (_frameSkipOffset + 1) % RAYCAST_SKIP_INTERVAL;

			int raycasts = 0;

			foreach (IMyEntity entity in candidates)
			{
				if (entity.Closed) continue;

				Vector3D p;
				IHitInfo hit;

				var grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					bool r = SurfaceSampler.TryGetRandomBlockOnSurface(grid, random, out p);
					if (!r) continue;

					MyAPIGateway.Physics.CastRay(engineer, entity.WorldMatrix.Translation, out hit, CollisionLayers.CollisionLayerWithoutCharacter);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;
					Drawing.RoundMarker(p, isBlocked ? Color.DimGray : Color.LimeGreen);
					if (isBlocked) continue;

					var type = grid.GridSizeEnum == MyCubeSize.Large ? ObjectType.LargeShip : ObjectType.SmallShip;
					SetLKS(entity, type, true);
				}

				var voxel = entity as MyVoxelBase;
				if (voxel != null)
				{
					if (voxel is MyPlanet) continue;

					bool r = SurfaceSampler.TryGetRandomSurfacePoint(voxel, random, out p);
					if (!r) continue;

					MyAPIGateway.Physics.CastRay(engineer, entity.WorldMatrix.Translation, out hit, CollisionLayers.CollisionLayerWithoutCharacter);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;
					//Drawing.RoundMarker(p, isBlocked ? Color.DimGray : Color.YellowGreen);
					if (isBlocked) continue;

					SetLKS(entity, ObjectType.Asteroid, true);
				}

				var floater = entity as IMyFloatingObject;
				if (floater != null)
				{
					// Staggered sampling: process every Nth floater, shifting the window each frame
					if (localSkipCounter++ % RAYCAST_SKIP_INTERVAL != 0) continue;

					MyAPIGateway.Physics.CastRay(engineer, entity.WorldMatrix.Translation, out hit, CollisionLayers.CollisionLayerWithoutCharacter);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;

					//Drawing.RoundMarker(entity.GetPosition(), isBlocked ? Color.DimGray : Color.Green);
					if (isBlocked) continue;

					SetLKS(entity, ObjectType.Floating, true);
				}
			}
		}

		public static void GetVisible(Vector3D engineer, StringBuilder result)
		{
			//MyConsole.Add($"raycasts: {raycasts} ", Color.Red);
			foreach (var v in lks.Values)
			{
				var delta = Time.Now - v.LastSeenAt;
				v.Visible = delta < 1.0;
				if (!v.Visible) continue;

				double distance = (engineer - new Vector3D(v.X, v.Y, v.Z)).Length();
				result.Append($"* {v.Type} '{v.DisplayName}' ({Commands.Distance(distance)})\n");
			}
		}

		public static void OnClose(IMyEntity e)
		{
			var grid = e as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded -= Grid_OnBlockAdded;
				grid.OnBlockRemoved -= Grid_OnBlockRemoved;
				grid.OnGridChanged -= Grid_OnGridChanged;
			}

			lks.Remove(e.EntityId);
		}

		public static void Grid_OnBlockAdded(IMySlimBlock block) { }

		public static void Grid_OnBlockRemoved(IMySlimBlock block) { }

		public static void Grid_OnGridChanged(IMyCubeGrid grid) { }
	}
}