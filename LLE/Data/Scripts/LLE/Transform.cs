using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public static class Transform
	{
		// Returns a matrix that transforms from model space to world space.
		internal static MatrixD GetModelToWorldMatrix(IMySlimBlock block)
		{
			Matrix orientMatrix;
			block.Orientation.GetMatrix(out orientMatrix);

			Vector3D worldCenter;
			block.ComputeWorldCenter(out worldCenter);

			MatrixD modelToWorld = new MatrixD(orientMatrix) * block.CubeGrid.WorldMatrix;
			modelToWorld.Translation = worldCenter;
			return modelToWorld;
		}

		internal static MatrixD GetWorldToModelMatrix(IMySlimBlock block)
		{
			var modelToWorld = GetModelToWorldMatrix(block);
			MatrixD invModelToWorld;
			MatrixD.Invert(ref modelToWorld, out invModelToWorld);
			return invModelToWorld;
		}

		// Transform a point from world space to model space.
		internal static Vector3D WorldToModel(IMySlimBlock block, Vector3D worldPoint)
		{
			return Vector3D.Transform(worldPoint, GetWorldToModelMatrix(block));
		}

		// Transform a point from model space to world space.
		internal static Vector3D ModelToWorld(IMySlimBlock block, Vector3D modelPoint)
		{
			return Vector3D.Transform(modelPoint, GetModelToWorldMatrix(block));
		}
	}
}
