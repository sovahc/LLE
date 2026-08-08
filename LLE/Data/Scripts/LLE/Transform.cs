using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public static class Transform
	{
		internal static MatrixD GetModelToWorldMatrix(IMySlimBlock block)
		{
			Matrix orientMatrix;
			block.Orientation.GetMatrix(out orientMatrix);

			Vector3D worldCenter;
			block.ComputeWorldCenter(out worldCenter);

			var modelToWorld = new MatrixD(orientMatrix) * block.CubeGrid.WorldMatrix;
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

		internal static Vector3D WorldToModel(IMySlimBlock block, Vector3D worldPoint)
		{
			return Vector3D.Transform(worldPoint, GetWorldToModelMatrix(block));
		}

		internal static Vector3D ModelToWorld(IMySlimBlock block, Vector3D modelPoint)
		{
			return Vector3D.Transform(modelPoint, GetModelToWorldMatrix(block));
		}
	}
}
