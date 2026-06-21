using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
   		private MyEntity3DSoundEmitter soundEmitter;
		private MyParticleEffect particleEffect;

		private void EnableSound(string sound)
		{
			if (soundEmitter == null)
			{
				soundEmitter = new MyEntity3DSoundEmitter(character as MyEntity);
			}
			if (soundEmitter != null)
			{
				soundEmitter.VolumeMultiplier = Constants.SoundVolume;
				soundEmitter.PlaySound(new MySoundPair(sound));
			}
		}

		private void EnableEffect(IMySlimBlock block, string particleName)
		{
			if (particleEffect == null)
			{
				MatrixD m = MatrixD.Identity;
				Vector3D pos = Vector3D.Zero;
				MyParticlesManager.TryCreateParticleEffect(particleName, ref m, ref pos, uint.MaxValue, out particleEffect);
			}
			if (particleEffect != null)
			{
				BoundingBoxD box;
				block.GetWorldBoundingBox(out box, false);
				particleEffect.WorldMatrix = box.Matrix;
				particleEffect.UserRadiusMultiplier = 4f;
			}
		}

		internal void DisableEffectAndSound()
		{	if(soundEmitter != null)
			{
				soundEmitter.StopSound(false);
				soundEmitter = null;
			}
			if(particleEffect != null)
			{
				particleEffect.Stop();
				particleEffect = null;
			}
		}

		/*internal void PlaySound(string name)
		{
			if (_hudEmitter == null)
			{
				_hudEmitter = new MyEntity3DSoundEmitter(null);
				_hudEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].ClearImmediate();
				_hudEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();
				_hudEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CueType].ClearImmediate();
				_hudEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ImplicitEffect].ClearImmediate();
			}

			_hudEmitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
			_hudEmitter.PlaySound(new MySoundPair(name), stopPrevious: false, alwaysHearOnRealistic: true, force2D: true);
		}*/
	}
}