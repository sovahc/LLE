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

		public static bool SphereVsConvex(
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

		public static bool ConvexVsConvex(List<Vector3> verticesA, List<Vector3> verticesB)
		{
			if (verticesA == null || verticesA.Count < 2) return false;
			if (verticesB == null || verticesB.Count < 2) return false;

			gjk.Reset();

			// Initial direction: centroid difference
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
			var vv = vertices;

			double maxDot = double.MinValue;
			Vector3D best = new Vector3D(vv[0].X, vv[0].Y, vv[0].Z);

			for (int i = 0; i < vv.Count; i++)
			{
				double dot = Vector3D.Dot(new Vector3D(vv[i].X, vv[i].Y, vv[i].Z), direction);
				if (dot > maxDot)
				{
					maxDot = dot;
					best = new Vector3D(vv[i].X, vv[i].Y, vv[i].Z);
				}
			}
			return best;
		}

		public static bool SphereVsSphere(
			Vector3D centerA, double radiusA,
			Vector3D centerB, double radiusB)
		{
			Vector3D diff = centerA - centerB;
			double sumRadii = radiusA + radiusB;
			return diff.LengthSquared() <= sumRadii * sumRadii;
		}

		public static bool SphereVsAABB(
			Vector3D sphereCenter, double sphereRadius,
			Vector3D cellMin, Vector3D cellMax)
		{
			// Find the closest point on the AABB to the sphere center
			double closestX = Math.Max(cellMin.X, Math.Min(sphereCenter.X, cellMax.X));
			double closestY = Math.Max(cellMin.Y, Math.Min(sphereCenter.Y, cellMax.Y));
			double closestZ = Math.Max(cellMin.Z, Math.Min(sphereCenter.Z, cellMax.Z));

			// Calculate the vector from the closest point to the sphere center
			double distanceX = sphereCenter.X - closestX;
			double distanceY = sphereCenter.Y - closestY;
			double distanceZ = sphereCenter.Z - closestZ;

			// Check if the squared distance is less than or equal to the squared radius
			return (distanceX * distanceX + distanceY * distanceY + distanceZ * distanceZ) <= (sphereRadius * sphereRadius);
		}

		// ! Modified ! Möller–Trumbore Ray-Triangle Intersection
		public static float? GetLineParallelogramIntersection(ref Line line, ref Parallelogram parallelogram)
		{
			// Compute vectors along two edges.
			Vector3 edge1, edge2;

			Vector3.Subtract(ref parallelogram.B, ref parallelogram.A, out edge1);
			Vector3.Subtract(ref parallelogram.C, ref parallelogram.A, out edge2);

			// Compute the determinant.
			Vector3 directionCrossEdge2;
			Vector3.Cross(ref line.Direction, ref edge2, out directionCrossEdge2);

			float determinant;
			Vector3.Dot(ref edge1, ref directionCrossEdge2, out determinant);

			// If the ray is parallel to the face plane, there is no collision.
			if (determinant > -ZeroEpsilon && determinant < ZeroEpsilon)
				return null;

			float inverseDeterminant = 1.0f / determinant;

			// Calculate the U parameter of the intersection point.
			Vector3 distanceVector;
			Vector3.Subtract(ref line.From, ref parallelogram.A, out distanceVector);

			float u;
			Vector3.Dot(ref distanceVector, ref directionCrossEdge2, out u);
			u *= inverseDeterminant;

			// Make sure it is inside the face
			if (u < 0 || u > 1) return null;

			// Calculate the V parameter of the intersection point.
			Vector3 distanceCrossEdge1;
			Vector3.Cross(ref distanceVector, ref edge1, out distanceCrossEdge1);

			float v;
			Vector3.Dot(ref line.Direction, ref distanceCrossEdge1, out v);
			v *= inverseDeterminant;

			// Make sure it is inside the face
			//if (v < 0 || u + v > 1) // original triangle code
			if (v < 0 || v > 1) return null;

			// Compute the distance along the ray to the parallelogram.
			float rayDistance;
			Vector3.Dot(ref edge2, ref distanceCrossEdge1, out rayDistance);
			rayDistance *= inverseDeterminant;

			// Is the face behind the ray origin?
			if (rayDistance < 0) return null;

			// Does the intersection point lie on the line (ray hasn't end, but line does)
			if (rayDistance > line.Length) return null;

			return rayDistance;
		}
	}
}
