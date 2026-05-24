using System;
using System.Collections.Generic;
using VRageMath;

namespace LLE
{
	public static class Geometry
	{
		public static void BoxToConvex(Vector3 he, List<Vector3> result)
		{
			result.Add(new Vector3( he.X,  he.Y,  he.Z));
			result.Add(new Vector3( he.X,  he.Y, -he.Z));
			result.Add(new Vector3( he.X, -he.Y,  he.Z));
			result.Add(new Vector3( he.X, -he.Y, -he.Z));
			result.Add(new Vector3(-he.X,  he.Y,  he.Z));
			result.Add(new Vector3(-he.X,  he.Y, -he.Z));
			result.Add(new Vector3(-he.X, -he.Y,  he.Z));
			result.Add(new Vector3(-he.X, -he.Y, -he.Z));
		}

		public static void CylinderToConvex(Vector3 a, Vector3 b, float R,
			List<Vector3> out_vertices, int segments = 16)
		{
			var vv = out_vertices;

			var axis = Vector3.Normalize(b - b);
			Vector3 right, localUp;
			Geometry.OrthonormalBasis(axis, out right, out localUp);

			for (int s = 0; s < segments; s++)
			{
				double angle = s * MathHelper.TwoPi / segments;
				double c = Math.Cos(angle), sn = Math.Sin(angle);
				Vector3 offset = (float)c * right * R + (float)sn * localUp * R;
				vv.Add(a + offset);
				vv.Add(b + offset);
			}
		}

		public static void OrthonormalBasis(Vector3 axis, out Vector3 right, out Vector3 up)
		{
			var perp = Math.Abs(Vector3.Dot(axis, Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			right = Vector3.Normalize(Vector3.Cross(axis, perp));
			up = Vector3.Cross(right, axis);
		}

		public static void OrthonormalBasis(Vector3D axis, out Vector3D right, out Vector3D up)
		{
			var perp = Math.Abs(Vector3D.Dot(axis, Vector3D.Up)) > 0.99 ? Vector3D.Forward : Vector3D.Up;
			right = Vector3D.Normalize(Vector3D.Cross(axis, perp));
			up = Vector3D.Cross(right, axis);
		}

		private static double Cross(Vector2D o, Vector2D a, Vector2D b)
		{
			return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
		}

		// Returns points on the convex hull in counter-clockwise order.
		// Note: the last point in the returned list is the same as the first one.
		public static List<Vector2D> ConvexHull(List<Vector2D> p)
		{
			int n = p.Count, k = 0;
			if (n <= 3)
				return p;

			p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

			Vector2D[] h = new Vector2D[2 * n];

			// Build lower hull
			for (int i = 0; i < n; ++i)
			{
				while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0)
					k--;
				h[k++] = p[i];
			}

			// Build upper hull
			for (int i = n - 1, t = k + 1; i > 0; --i)
			{
				while (k >= t && Cross(h[k - 2], h[k - 1], p[i - 1]) <= 0)
					k--;
				h[k++] = p[i - 1];
			}

			var result = new List<Vector2D>(k - 1);
			for (int i = 0; i < k - 1; ++i)
				result.Add(h[i]);
			return result;
		}
	}
}
