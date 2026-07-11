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

		public static void BoxToParallelograms(Vector3 he, List<Parallelogram> result)
		{
			var A = new Vector3(-he.X, -he.Y, -he.Z);
			var B = new Vector3(+he.X, -he.Y, -he.Z);
			var C = new Vector3(-he.X, +he.Y, -he.Z);
			var D = new Vector3(-he.X, -he.Y, +he.Z);
			
			result.Add(new Parallelogram(A, B, C));
			result.Add(new Parallelogram(A, C, D));
			result.Add(new Parallelogram(A, D, B));

			A = new Vector3(+he.X, +he.Y, +he.Z);
			B = new Vector3(-he.X, +he.Y, +he.Z);
			C = new Vector3(+he.X, -he.Y, +he.Z);
			D = new Vector3(+he.X, +he.Y, -he.Z);

			result.Add(new Parallelogram(A, B, C));
			result.Add(new Parallelogram(A, C, D));
			result.Add(new Parallelogram(A, D, B));
		}

		public static void CylinderToConvex(Vector3 a, Vector3 b, float R,
			List<Vector3> out_vertices, int segments = 16)
		{
			var vv = out_vertices;
			vv.Clear();

			var axis = Vector3.Normalize(b - a);
			Vector3 right, localUp;
			OrthonormalBasis(axis, out right, out localUp);

			for (int s = 0; s < segments; s++)
			{
				double angle = s * MathHelper.TwoPi / segments;
				double c = Math.Cos(angle), sn = Math.Sin(angle);
				Vector3 offset = (float)c * right * R + (float)sn * localUp * R;
				vv.Add(a + offset);
				vv.Add(b + offset);
			}
		}

		// Generates a full icosphere: subdivided icosahedron projected onto a sphere.
		// Vertex-at-pole orientation (poles along Z, clean equator at depth >= 1).
		// subdivisions=0 → 12 vertices, =1 → 42, =2 → 162.
		public static void Icosphere(float radius, int subdivisions, List<Vector3> result)
		{
			result.Clear();

			float r = 2f / (float)Math.Sqrt(5);
			float h = 1f / (float)Math.Sqrt(5);
			float step = 2f * (float)Math.PI / 5f;

			Vector3[] icoVerts = new Vector3[12];
			icoVerts[0] = new Vector3(0, 0, 1);   // north pole
			for (int k = 0; k < 5; k++)           // upper ring
			{
				float a = k * step;
				icoVerts[1 + k] = new Vector3(r * (float)Math.Cos(a), r * (float)Math.Sin(a), h);
			}
			for (int k = 0; k < 5; k++)           // lower ring (offset by 36°)
			{
				float a = k * step + (float)Math.PI / 5f;
				icoVerts[6 + k] = new Vector3(r * (float)Math.Cos(a), r * (float)Math.Sin(a), -h);
			}
			icoVerts[11] = new Vector3(0, 0, -1); // south pole

			int[][] faces = {
				// 5 around north pole
				new[]{ 0, 1, 2 }, new[]{ 0, 2, 3 }, new[]{ 0, 3, 4 }, new[]{ 0, 4, 5 }, new[]{ 0, 5, 1 },
				// 10 connecting upper and lower rings
				new[]{ 1, 6, 2 }, new[]{ 2, 6, 7 }, new[]{ 2, 7, 3 }, new[]{ 3, 7, 8 }, new[]{ 3, 8, 4 },
				new[]{ 4, 8, 9 }, new[]{ 4, 9, 5 }, new[]{ 5, 9, 10 }, new[]{ 5, 10, 1 }, new[]{ 1, 10, 6 },
				// 5 around south pole
				new[]{ 11, 7, 6 }, new[]{ 11, 8, 7 }, new[]{ 11, 9, 8 }, new[]{ 11, 10, 9 }, new[]{ 11, 6, 10 },
			};

			foreach (var f in faces)
			{
				SubdivideFace(icoVerts[f[0]], icoVerts[f[1]], icoVerts[f[2]],
					subdivisions, radius, result);
			}
		}

		// Icosphere-based capsule: top hemisphere → b, mirrored bottom → a.
		// subdivisions=1 → 52 capsule vertices.
		public static void CapsuleToConvex(Vector3 a, Vector3 b, float R,
			List<Vector3> out_vertices, int subdivisions = 1)
		{
			var vv = out_vertices;
			vv.Clear();

			// 1. Full icosphere in local space (Z = capsule axis)
			var sphere = new List<Vector3>();
			Icosphere(R, subdivisions, sphere);

			// 2. Capsule frame
			var axis = Vector3.Normalize(b - a);
			Vector3 right, up;
			OrthonormalBasis(axis, out right, out up);

			// 3. Keep top hemisphere (Z >= -epsilon), shift up to b, mirror down to a
			float epsilon = R * 0.001f;

			foreach (var v in sphere)
			{
				if (v.Z < -epsilon)
					continue;

				// Top: local (X, Y, Z + halfHeight) → world at b
				vv.Add(b + right * v.X + up * v.Y + axis * v.Z);

				// Mirror: local (X, Y, -(Z + halfHeight)) → world at a
				vv.Add(a + right * v.X + up * v.Y - axis * v.Z);
			}
		}

		private static void SubdivideFace(Vector3 v0, Vector3 v1, Vector3 v2, int depth,
			float radius, List<Vector3> result)
		{
			if (depth <= 0)
			{
				AddUnique(v0 * radius, result);
				AddUnique(v1 * radius, result);
				AddUnique(v2 * radius, result);
				return;
			}

			var m01 = Vector3.Normalize(v0 + v1);
			var m12 = Vector3.Normalize(v1 + v2);
			var m20 = Vector3.Normalize(v2 + v0);

			SubdivideFace(v0, m01, m20, depth - 1, radius, result);
			SubdivideFace(m01, v1, m12, depth - 1, radius, result);
			SubdivideFace(m20, m12, v2, depth - 1, radius, result);
			SubdivideFace(m01, m12, m20, depth - 1, radius, result);
		}

		private static void AddUnique(Vector3 v, List<Vector3> result)
		{
			for (int i = 0; i < result.Count; i++)
			{
				if (result[i].X == v.X && result[i].Y == v.Y && result[i].Z == v.Z)
					return;
			}
			result.Add(v);
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
		// Note: sorts the input list in-place as a side effect.
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

		public static void SweptCapsule(double halfHeight, double radius, double halfSweep, List<Vector3D> result,
			int segments = 2, int segments2 = 3)
		{
			result.Clear();

			for (int lat = 0; lat <= segments; lat++)
			{
				double phi = lat * Math.PI / 2.0 / segments;
				double sinPhi = Math.Sin(phi);
				double cosPhi = Math.Cos(phi);

				for (int lon = 0; lon <= segments2; lon++)
				{
					double theta = lon * Math.PI / segments2;
					double cosTheta = Math.Cos(theta);
					double sinTheta = Math.Sin(theta);

					double x = sinPhi * cosTheta * radius;
					double z = sinPhi * sinTheta * radius;
					double y = cosPhi * radius;

					result.Add(new Vector3D(x, +halfHeight + y, +halfSweep + z));
					result.Add(new Vector3D(x, -halfHeight - y, +halfSweep + z));
					result.Add(new Vector3D(x, +halfHeight + y, -halfSweep - z));
					result.Add(new Vector3D(x, -halfHeight - y, -halfSweep - z));
				}
			}
		}

		public static void MinMax(List<Vector3D> points, out Vector3D min, out Vector3D max)
		{
			min = Vector3D.MaxValue;
			max = Vector3D.MinValue;

			foreach(var p in points)
			{	if(min.X > p.X) min.X = p.X;
				if(min.Y > p.Y) min.Y = p.Y;
				if(min.Z > p.Z) min.Z = p.Z;

				if(max.X < p.X) max.X = p.X;
				if(max.Y < p.Y) max.Y = p.Y;
				if(max.Z < p.Z) max.Z = p.Z;
			}
		}

		public static List<Vector3D> FloatToDouble(List<Vector3> vv)
		{
			var result = new List<Vector3D>(vv.Count);
			foreach(var v in vv)
				result.Add(new Vector3D(v));
			return result;
		}
	}
}
