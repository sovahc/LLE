using System;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	class Vision
	{
		private static Random random = new Random();
		private const int RAYCAST_SKIP_INTERVAL = 10;
		private static int _frameSkipOffset;
		private static readonly StringBuilder visionReport = new StringBuilder();
		private static double nextVisionReport;

		internal static readonly Dictionary<long, LastKnownState> lks = new Dictionary<long, LastKnownState>();

		private static LastKnownState SetLKS(IMyEntity entity, bool isVisible)
		{
			ObjectType type = ObjectType.Unknown;

			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{	type = grid.GridSizeEnum == MyCubeSize.Large ? ObjectType.LargeShip : ObjectType.SmallShip;
			}
			var voxel = entity as MyVoxelBase;
			if (voxel != null)
			{	type = ObjectType.Asteroid;
			}
			var floater = entity as IMyFloatingObject;
			if (floater != null)
			{	type = ObjectType.Floating;				
			}

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
				state.Report = true;

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

			return state;
		}

		public static void Initialize()
		{	nextVisionReport = Time.Now + 1;			
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

					MyAPIGateway.Physics.CastRay(engineer, p, out hit, CollisionLayers.CollisionLayerWithoutCharacter);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;
					//Drawing.RoundMarker(p, isBlocked ? Color.DimGray : Color.LimeGreen);
					if (isBlocked) continue;

					SetLKS(entity, true);
				}

				var voxel = entity as MyVoxelBase;
				if (voxel != null)
				{
					if (voxel is MyPlanet) continue;

					bool r = SurfaceSampler.TryGetRandomSurfacePoint(voxel, random, out p);
					if (!r) continue;

					MyAPIGateway.Physics.CastRay(engineer, p, out hit, CollisionLayers.CollisionLayerWithoutCharacter);
					++raycasts;

					bool isBlocked = hit != null && hit.HitEntity != entity;
					//Drawing.RoundMarker(p, isBlocked ? Color.DimGray : Color.YellowGreen);
					if (isBlocked) continue;

					SetLKS(entity, true);
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

					SetLKS(entity, true);
				}
			}
		}

		public static string VisionReport(Vector3D engineer)
		{
			if(Time.Now < nextVisionReport) return null;
			nextVisionReport = Time.Now + 1;

			foreach (var v in lks.Values)
			{
				var delta = Time.Now - v.LastSeenAt;
				v.Visible = delta < 1.0;
				if (v.Visible || v.Closed)
				{	if (!v.Report) continue;
					v.Report = false;

					string state = v.Closed ? "GONE" : "NEW";
					double distance = (engineer - new Vector3D(v.X, v.Y, v.Z)).Length();
					visionReport.Append($"* {state} {v.Type} '{v.DisplayName}' ({Commands.Distance(distance)})\n");
				}
			}

			// Remove only one entity to avoid modifying collection during enumeration
			foreach (var v in lks.Values)
			{	if(!v.Closed) continue;
			
				lks.Remove(v.EntityId);
				break;
			}

			if(visionReport.Length == 0) return null;
			string r = visionReport.ToString();
			visionReport.Clear();
			return r;
		}

		internal static void OnClose(IMyCubeGrid grid)
		{
			LastKnownState state;

			if (!lks.TryGetValue(grid.EntityId, out state)) return;
			
			if(!state.Closed)
			{	state.Closed = true;
				state.Report = true;
			}
		}

		internal static void OnBlockAdded(IMySlimBlock block)
		{	var gn = block.CubeGrid.DisplayName;
			visionReport.Append($"* BLOCK {Commands.BlockName(block)} ADDED TO '{gn}' AT {Commands.IJK(block.Min)}\n");
		}

		internal static void OnBlockRemoved(IMySlimBlock block)
		{	var gn = block.CubeGrid.DisplayName;
			visionReport.Append($"* BLOCK {Commands.BlockName(block)} REMOVED FROM '{gn}' AT {Commands.IJK(block.Min)}\n");
		}

		internal static void OnGridChanged(IMyCubeGrid grid) { }

		internal static void OnGridSplit(IMyCubeGrid original, IMyCubeGrid created)
		{
			var state = SetLKS(created, false);
			state.Report = false; // overwrite

			visionReport.Clear(); // hack: stop BLOCK REMOVED spam
			visionReport.Append($"* GRID SPLIT '{original.DisplayName}' / '{created.DisplayName}'\n");
		}

		internal static void OnGridMerge(IMyCubeGrid a, IMyCubeGrid b)
		{
			var state = SetLKS(b, false);
			state.Closed = true;
			state.Report = false;

			visionReport.Clear(); // hack: stop BLOCK ADDED spam
			visionReport.Append($"* GRID MERGE '{a.DisplayName}' / '{b.DisplayName}'\n");
		}
	}
}
