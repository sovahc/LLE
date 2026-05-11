using VRageMath;
using System.Collections.Generic;
using System;

namespace LLE {
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
}
}
