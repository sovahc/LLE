using VRageMath;
using System.Collections.Generic;
using System;

public static class Ellipsoid
{
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

    public static List<Vector2> ProjectEllipsoid(Vector3D center, Vector3D axisU, Vector3D axisV, Vector3D axisW, MatrixD viewMatrix, int segments = 32)
    {
        var points = new List<Vector2>(segments + 1);

        Vector3D viewCenter = Vector3D.Transform(center, viewMatrix);
        Vector3D viewU = Vector3D.TransformNormal(axisU, viewMatrix);
        Vector3D viewV = Vector3D.TransformNormal(axisV, viewMatrix);
        Vector3D viewW = Vector3D.TransformNormal(axisW, viewMatrix);

        return GenerateProjectedEllipsePoints(viewCenter, viewU, viewV, viewW, segments);
    }

    private static List<Vector2> GenerateProjectedEllipsePoints(Vector3D center, Vector3D u, Vector3D v, Vector3D w, int segments)
    {
        double ux = u.X, uy = u.Y;
        double vx = v.X, vy = v.Y;
        double wx = w.X, wy = w.Y;

        double c00 = ux*ux + vx*vx + wx*wx;
        double c01 = ux*uy + vx*vy + wx*wy;
        double c11 = uy*uy + vy*vy + wy*wy;

        double tr = c00 + c11;
        double det = c00 * c11 - c01 * c01;
        double disc = Math.Sqrt(Math.Max(0, (c00 - c11)*(c00 - c11) + 4*c01*c01));

        double eig1 = 0.5 * (tr + disc);
        double eig2 = 0.5 * (tr - disc);

        double semiMajor = Math.Sqrt(eig1);
        double semiMinor = Math.Sqrt(eig2);

        double angle = 0;
        if (Math.Abs(c01) > 1e-9)
        {
            angle = 0.5 * Math.Atan2(2 * c01, c00 - c11);
        }
        else if (c00 < c11)
        {
            angle = Math.PI / 2;
        }

        var points = new List<Vector2>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double t = i * MathHelper.TwoPi / segments;
            double cosT = Math.Cos(t);
            double sinT = Math.Sin(t);

            double x = semiMajor * cosT;
            double y = semiMinor * sinT;

            double cosA = Math.Cos(angle);
            double sinA = Math.Sin(angle);

            double rx = x * cosA - y * sinA;
            double ry = x * sinA + y * cosA;

            points.Add(new Vector2((float)(center.X + rx), (float)(center.Y + ry)));
        }

        return points;
    }
}
