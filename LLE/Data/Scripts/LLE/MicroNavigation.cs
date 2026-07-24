using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	class MicroNavigation
	{
		private const double arrivalThreshold = 0.5;
		private List<Commands.PathNode> path = new List<Commands.PathNode>();
		private int currentWaypointIndex;
		private double StuckTimer;

		public void Fly(List<Commands.PathNode> path)
		{
			this.path = path;
			currentWaypointIndex = 0;
			StuckTimer = 0;
			Stuck = false;
		}

		public bool Arrived()
		{
			return currentWaypointIndex >= path.Count;
		}

		public void Stop()
		{	path.Clear();		
		}

		public void DrawPath()
		{
			for(int i = 0; i < path.Count; ++i)
				Drawing.RoundMarker(path[i].v, i > currentWaypointIndex ? Color.HotPink : Color.Gray);
		}

		public bool Stuck;
		public bool ShortSegment;
		public Commands.PathNode Target;
		public Commands.PathNode Done;

		public Vector3D ComputeDesiredVelocity(Vector3D currentPosition, Vector3D currentVelocity, double DeltaTime = 1.0 / 60)
		{
			if (Arrived()) return Vector3D.Zero;

			Target = path[currentWaypointIndex];

			Vector3D toTarget = Target.v - currentPosition;
			double distance = toTarget.Length();

			ShortSegment = distance < 4.0;

			if (distance < arrivalThreshold)
			{	Done = Target;
				++currentWaypointIndex;
				return ComputeDesiredVelocity(currentPosition, currentVelocity);
			}

			double maximalSpeed = 20;
			double desiredSpeed = Math.Min(maximalSpeed, distance * 3.0); // Arrive behavior

			Vector3D desiredVelocity = toTarget.Normalized() * desiredSpeed;

			if(currentVelocity.LengthSquared() < 1)
			{	StuckTimer += DeltaTime;
				if (StuckTimer > 3.0)
				{	Stuck = true;
				}
			}
			else StuckTimer = 0;

			// Smooth acceleration (PD controller)
			Vector3D velocityError = desiredVelocity - currentVelocity;
			var P_coefficient = 5.0;
			Vector3D acceleration = velocityError * P_coefficient;

			return currentVelocity + acceleration * DeltaTime;
		}

		public Vector3 ComputeMoveInput(Vector3D desiredVelocity, Vector3D currentVelocity, MatrixD worldMatrix)
		{
			Vector3D thrust = desiredVelocity - currentVelocity;
			if (thrust.LengthSquared() < 0.01) return Vector3.Zero;

			Vector3D localThrust = Vector3D.TransformNormal(thrust, MatrixD.Transpose(worldMatrix));
			Vector3 move = (Vector3)localThrust;
			
			if (move.LengthSquared() > 1) move.Normalize();
			return move;
		}
	}

	class DampedSpringController
	{
		const double omega = 4.0;
		const double zeta = 1.0;

		double yawPos, yawVel;
		double pitchPos, pitchVel;
		double rollPos, rollVel;

		public void Update(Vector3D botPos, Vector3D Forward, Vector3D Up, Vector3D targetPoint, Vector3D gridUp,
			double deltaTime, bool headFirst, out Vector2 rotation, out float roll)
		{
			var toTarget = targetPoint - botPos;
			gridUp.Normalize();

			double relativePitch, relativeYaw, relativeRoll;

			if(headFirst)
			{
				// Head-first: align Up toward target via pitch+roll, align Forward toward gridUp via yaw.
				// Each error must match its rotation axis:
				//   pitch (around Right) tilts Up toward Forward
				//   roll  (around Forward) tilts Up toward Right
				//   yaw   (around Up) rotates Forward toward gridUp
				double toTargetLen = toTarget.Length();
				if(toTargetLen < 0.001)
				{ 	rotation = Vector2.Zero; roll = 0; return; 
				}
				Vector3D toTargetN = toTarget / toTargetLen;
				Vector3D R = Vector3D.Cross(Up, Forward);

				// Pitch: angle between Up and target in the U-F plane (negated for SE look-down convention)
				relativePitch = -Math.Atan2(Vector3D.Dot(toTargetN, Forward), Vector3D.Dot(toTargetN, Up));

				// Roll: angle between Up and target in the U-R plane (negated for SE roll convention)
				relativeRoll = -Math.Atan2(Vector3D.Dot(toTargetN, R), Vector3D.Dot(toTargetN, Up));

				// Yaw: angle between Forward and gridUp, measured around Up
				relativeYaw = 0;
				Vector3D Gperp = Vector3D.Dot(gridUp, Up) * Up - gridUp;
				if(Gperp.LengthSquared() > 0.001)
				{
					Gperp = Vector3D.Normalize(Gperp);
					var cross = Vector3D.Cross(Forward, Gperp);
					relativeYaw = Math.Atan2(Vector3D.Dot(cross, Up), Vector3D.Dot(Forward, Gperp));
				}
			}
			else
			{
				// Normal mode: align Forward toward target (pitch+yaw), align Up toward gridUp (roll)
				var projUp = Vector3D.Dot(toTarget, gridUp) * gridUp;
				var horizontal = toTarget - projUp;
				double horizDist = Math.Max(horizontal.Length(), 0.1);

				// Absolute target angle and current bot angle
				double targetPitch = Math.Atan2(Vector3D.Dot(toTarget, gridUp), horizDist);
				double currentPitch = Math.Asin(Vector3D.Dot(Forward, gridUp));
				relativePitch = targetPitch - currentPitch;

				relativeYaw = 0;
				if (horizontal.LengthSquared() > 0.001)
				{
					var forwardFlat = Forward - Vector3D.Dot(Forward, gridUp) * gridUp;
					if (forwardFlat.LengthSquared() > 0.001)
					{
						forwardFlat.Normalize();
						horizontal.Normalize();
						var cross = Vector3D.Cross(forwardFlat, horizontal);
						relativeYaw = Math.Atan2(Vector3D.Dot(cross, gridUp), Vector3D.Dot(forwardFlat, horizontal));
					}
				}

				var crossRoll = Vector3D.Cross(Up, gridUp);
				double sinRoll = Vector3D.Dot(crossRoll, Forward);
				double cosRoll = Vector3D.Dot(Up, gridUp);
				relativeRoll = Math.Atan2(sinRoll, cosRoll);
			}

			// 4. SPRING UPDATE
			// We "anchor" the spring target to its current position + error.
			// This creates a stable absolute target that tracks the error perfectly without drift.

			double oldYawPos = yawPos;
			UpdateSpring(ref yawPos, ref yawVel, yawPos + relativeYaw, deltaTime, true);

			double oldPitchPos = pitchPos;
			UpdateSpring(ref pitchPos, ref pitchVel, pitchPos + relativePitch, deltaTime, false);

			double oldRollPos = rollPos;
			UpdateSpring(ref rollPos, ref rollVel, rollPos + relativeRoll, deltaTime, true);

			// 5. DELTA FORMATION (Controller / mouse input)
			// The game needs delta (how many degrees to turn this frame), not velocity.
			float deltaPitch = (float)MathHelper.ToDegrees(pitchPos - oldPitchPos);
			float deltaYaw = (float)MathHelper.ToDegrees(yawPos - oldYawPos);
			float deltaRoll = (float)MathHelper.ToDegrees(rollPos - oldRollPos);

			rotation = new Vector2(-deltaPitch, -deltaYaw);
			roll = deltaRoll;
		}

		void UpdateSpring(ref double pos, ref double vel, double target, double dt, bool wrapAngle)
		{
			double x0 = pos - target;
			if (wrapAngle)
			{
				x0 = Math.Atan2(Math.Sin(x0), Math.Cos(x0));
			}

			double omegaZeta = omega * zeta;
			double expTerm = Math.Exp(-omegaZeta * dt);
			double timeExp = dt * expTerm;
			double timeExpFreq = timeExp * omega;

			double xNew = x0 * (timeExpFreq + expTerm) + vel * timeExp;
			double vNew = x0 * (-omega * timeExpFreq) + vel * (-timeExpFreq + expTerm);

			pos = target + xNew;
			vel = vNew;
		}
	}
}