using System.Collections.Generic;
using VRageMath;

namespace LLE
{
    public static class Geometry
    {
        // 3D cross product of OA and OB vectors (z-component of their 2D cross product).
        // Returns positive if OAB makes a counter-clockwise turn,
        // negative for clockwise, zero if collinear.
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
