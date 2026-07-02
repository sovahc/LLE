using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public partial class Collisions
	{
		public static void GetInteractionPoints(IMySlimBlock block,
			List<Vector3I> inventoryIP,
			List<Vector3I> medblockIP)
		{
			Debug.linesRed.Clear();
			Debug.linesGray.Clear();

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return;

			List<float> inventoryDistance = new List<float>();
			List<float> medblockDistance = new List<float>();

			var grid = block.CubeGrid;

			var min = block.Min-1;
			var max = block.Max+1;

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if(ijkBlock != null && !CenterIsFree(ijkBlock, ijk)) continue;

				Vector3D worldFrom = grid.GridIntegerToWorld(ijk);
				Vector3 modelFrom = Transform.WorldToModel(block, worldFrom);

				foreach (var detector in geometry.Detectors)
				{	
					bool inventory =
						detector.Name.StartsWith("conveyor_") ||
						detector.Name.StartsWith("inventory_") || 
						detector.Name.StartsWith("cockpit_");
					bool medblock = detector.Name.StartsWith("block_");

					if(!inventory && !medblock) continue;

					var detectorCenter = detector.Transform.Translation;
					var line = new Line(modelFrom, detectorCenter);
					
					if(line.Length > Constants.MaxInteractionDistance) continue;

					float minIntersection = float.MaxValue;

					foreach(var p in detector.ForRaycast)
					{	var lp = p;
						var f = Intersections.GetLineParallelogramIntersection(ref line, ref lp);
						if(!f.HasValue) continue;

						if(f.Value < minIntersection) minIntersection = f.Value;
					}

					if(minIntersection >= float.MaxValue) continue;

					var clippedByDetector = new Line(line.From, line.From + line.Direction * minIntersection);
					var worldLine = new LineD(worldFrom, Transform.ModelToWorld(block, clippedByDetector.To));

					if(LineIntersectsGridGeometry(grid, worldLine, min, max, null))
					{	Drawing.RoundMarker(worldLine.To, Color.Gray);
						continue;
					}

					Drawing.RoundMarker(worldLine.To, Color.Green);

					Debug.linesRed.Add(worldLine);

					if(inventory)
					{	inventoryIP.Add(ijk);
						inventoryDistance.Add(minIntersection);
					}
					if(medblock)
					{	medblockIP.Add(ijk);
						medblockDistance.Add(minIntersection);
					}
				}
			}

			SelectNearest(inventoryIP, inventoryDistance);
			SelectNearest(medblockIP, medblockDistance);
		}

		private static void SelectNearest(List<Vector3I> ijk, List<float> distance)
		{
			if (distance.Count == 0) return;

			float min = distance[0];
			for (int n = 1; n < distance.Count; ++n)
				if (distance[n] < min) min = distance[n];

			float threshold = min + 0.25f;

			for (int n = distance.Count - 1; n >= 0; --n)
			{	if (distance[n] <= threshold) continue;

				ijk.RemoveAt(n);
				distance.RemoveAt(n);
			}
		}

		public static void GetGrindWeldPoints(IMySlimBlock block, List<Vector3I> grindWeldIP)
		{
			Debug.linesRed.Clear();
			Debug.linesGray.Clear();

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return;

			var grid = block.CubeGrid;

			var min = block.Min-1;
			var max = block.Max+1;

			var intersected = new List<IMySlimBlock>();

			var iterator = new Vector3I_RangeIterator(ref min, ref max);
			for (; iterator.IsValid(); iterator.MoveNext())
			{
				var ijk = iterator.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if(ijkBlock != null && !CenterIsFree(ijkBlock, ijk)) continue;

				Vector3D worldFrom = grid.GridIntegerToWorld(ijk);

				foreach(var direction in Constants.SixDirections)
				{	
					var test = ijk + direction;
					//if(!test.IsInsideInclusiveEnd(min, max)) continue;
					if(grid.GetCubeBlock(test) != block) continue;

					Vector3D worldTo = grid.GridIntegerToWorld(test);

					intersected.Clear();

					LineIntersectsGridGeometry(grid, new LineD(worldFrom, worldTo), min, max, intersected);
					
					if(intersected.Count == 1 && intersected[0] == block)
					{
						grindWeldIP.Add(ijk);
						break;
					}
				}
			}
		}
	}
}
