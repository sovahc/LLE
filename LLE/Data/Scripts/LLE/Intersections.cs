using VRageMath;
using System.Collections.Generic;

namespace LLE
{
	public static class Intersections
	{
		private const double ZeroEpsilon = 1e-6;
		private const double ConvergenceRelativeEpsilon = 1e-6;
		private const double IntersectionDistanceFactor = 1e-8;

		private static GjkD gjk = new GjkD();

		public static bool SphereIntersectsConvex(
			Vector3D sphereCenter, double sphereRadius,
			List<Vector3> convexVertices)
		{
			if (convexVertices == null || convexVertices.Count < 3)
				return false;

			gjk.Reset();

			// Initial direction: from sphere center toward convex centroid
			Vector3D direction = Vector3D.Zero;
			foreach (var v in convexVertices)
				direction += new Vector3D(v.X, v.Y, v.Z);
			direction /= convexVertices.Count;
			direction -= sphereCenter;

			// Degenerate: sphere center == convex centroid
			if (direction.LengthSquared() < ZeroEpsilon * ZeroEpsilon)
				direction = new Vector3D(1, 0, 0);

			double prevDistSq = double.MaxValue;
			double distSq = 0;

			do
			{
				// Support point of convex in +direction
				Vector3D convexSupport = GetConvexSupport(convexVertices, direction);

				// Support point of sphere in -direction
				Vector3D sphereSupport;
				double dirLen = direction.Length();
				if (dirLen < ZeroEpsilon)
					return true; // origin is inside
				sphereSupport = sphereCenter - sphereRadius * (direction / dirLen);

				// Minkowski difference point
				Vector3D minkowskiPoint = convexSupport - sphereSupport;

				// If projection is positive, bodies intersect
				double proj = Vector3D.Dot(direction, minkowskiPoint);
				if (proj > 0)
					return true;

				if (!gjk.AddSupportPoint(ref minkowskiPoint))
					return false; // no convergence, no intersection

				direction = gjk.ClosestPoint;
				distSq = direction.LengthSquared();

				// Convergence check
				if ((prevDistSq - distSq) / prevDistSq < ConvergenceRelativeEpsilon)
					return false;
				prevDistSq = distSq;

			} while (!gjk.FullSimplex && distSq > IntersectionDistanceFactor * gjk.MaxLengthSquared);

			return true;
		}

		private static Vector3D GetConvexSupport(List<Vector3> vertices, Vector3D direction)
		{
			double maxDot = double.MinValue;
			Vector3D best = new Vector3D(vertices[0].X, vertices[0].Y, vertices[0].Z);

			for (int i = 0; i < vertices.Count; i++)
			{
				double dot = Vector3D.Dot(new Vector3D(vertices[i].X, vertices[i].Y, vertices[i].Z), direction);
				if (dot > maxDot)
				{
					maxDot = dot;
					best = new Vector3D(vertices[i].X, vertices[i].Y, vertices[i].Z);
				}
			}
			return best;
		}
	}
}