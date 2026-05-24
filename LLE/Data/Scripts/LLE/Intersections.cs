using System;
using System.Collections.Generic;
using VRageMath;

namespace LLE
{
	public static class Intersections
	{
		private const double ZeroEpsilon = 1e-6;
		private const double ConvergenceRelativeEpsilon = 1e-6;
		private const double IntersectionDistanceFactor = 1e-8;

		private static readonly GjkD gjk = new GjkD();

		public static bool SphereIntersectsConvex(
			Vector3D sphereCenter, double sphereRadius,
			List<Vector3> convexVertices)
		{
			if (convexVertices == null || convexVertices.Count < 3) // 4?
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
			double distSq;

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

				// If projection is non-positive, new support point did not advance past origin
				double proj = Vector3D.Dot(direction, minkowskiPoint);
				if (proj < -ZeroEpsilon)
					return false;

				if (!gjk.AddSupportPoint(ref minkowskiPoint))
					return false; // no convergence, no intersection

				direction = -gjk.ClosestPoint;
				distSq = direction.LengthSquared();

				if (distSq <= ZeroEpsilon)
					return true; // Point is inside or on the boundary

				// Convergence check
				double improvement = prevDistSq - distSq;
				if (improvement < ConvergenceRelativeEpsilon * Math.Max(prevDistSq, 1.0))
					return false; // Algorithm stalled, no intersection

				prevDistSq = distSq;

			} while (distSq > IntersectionDistanceFactor * gjk.MaxLengthSquared);
			//} while (distSq > Math.Max(IntersectionDistanceFactor * gjk.MaxLengthSquared, ZeroEpsilon)); //?

			return true;
		}

		public static bool ConvexIntersectsConvex(List<Vector3> verticesA, List<Vector3> verticesB)
		{
			if (verticesA == null || verticesA.Count < 3) return false;
			if( verticesB == null || verticesB.Count < 3) return false;

			gjk.Reset();

			// Начальное направление: разность центроидов
			Vector3D centroidA = Vector3D.Zero;
			foreach (var v in verticesA) centroidA += new Vector3D(v.X, v.Y, v.Z);
			centroidA /= verticesA.Count;

			Vector3D centroidB = Vector3D.Zero;
			foreach (var v in verticesB) centroidB += new Vector3D(v.X, v.Y, v.Z);
			centroidB /= verticesB.Count;

			Vector3D direction = centroidA - centroidB;
			if (direction.LengthSquared() < ZeroEpsilon * ZeroEpsilon)
				direction = new Vector3D(1, 0, 0);

			double prevDistSq = double.MaxValue;
			double distSq;

			do
			{
				Vector3D supportA = GetConvexSupport(verticesA, direction);
				Vector3D supportB = GetConvexSupport(verticesB, -direction);
				Vector3D minkowskiPoint = supportA - supportB;

				double proj = Vector3D.Dot(direction, minkowskiPoint);
				if (proj < -ZeroEpsilon)
					return false;

				if (!gjk.AddSupportPoint(ref minkowskiPoint))
					return false;

				direction = -gjk.ClosestPoint;
				distSq = direction.LengthSquared();

				if (distSq <= ZeroEpsilon)
					return true;

				double improvement = prevDistSq - distSq;
				if (improvement < ConvergenceRelativeEpsilon * Math.Max(prevDistSq, 1.0))
					return false;

				prevDistSq = distSq;

			} while (distSq > IntersectionDistanceFactor * gjk.MaxLengthSquared);

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

