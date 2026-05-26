using System;
using System.Collections.Generic;
using VRageMath;

namespace LLE
{
	class MicroNavigation
	{
		private const double lookaheadDistance = 0.5;
		private const double pathCorrectionStrength = 0.1;
		private const double arrivalThreshold = 0.25;
		private const double maxVelocity = 10.0;
		private List<Vector3D> path;
		private int currentWaypointIndex;
		private StuckDetector stuckDetector = new StuckDetector();

		public void Fly(List<Vector3D> path)
		{
			this.path = path;
			currentWaypointIndex = 0;
			stuckDetector.Reset();
			Stuck = false;
		}

		public bool Arrived()
		{
			return currentWaypointIndex >= path.Count;
		}

		public bool Stuck;

		public Vector3D ComputeDesiredVelocity(Vector3D currentPosition, Vector3D currentVelocity, double DeltaTime = 1.0 / 60)
		{
			if (Arrived()) return Vector3D.Zero;

			// 1. Find the lookahead point on the path ahead
			Vector3D targetPoint = FindLookaheadPoint(currentPosition, lookaheadDistance);
			
			// 1b. Find nearest point on current path segment (for correction)
			Vector3D nearestOnPath = FindNearestPointOnPath(currentPosition);

			// 2. Compute desired velocity (Seek + Arrive)
			Vector3D toTarget = targetPoint - currentPosition;
			double distance = toTarget.Length();

			if (distance < arrivalThreshold)
			{
				// Very close to waypoint, switch to next
				currentWaypointIndex++;
				return ComputeDesiredVelocity(currentPosition, currentVelocity);
			}

			// Maximum speed depends on distance (slow down before turns)
			double maxSpeed = ComputeMaxSpeedForSegment(currentWaypointIndex);
			double desiredSpeed = Math.Min(maxSpeed, distance * 2.0); // Arrive behavior
			
			// 3. Dual-target steering: main target + path correction
			Vector3D mainVelocity = toTarget.Normalized() * desiredSpeed;
			Vector3D correctionVelocity = (nearestOnPath - currentPosition) * pathCorrectionStrength * desiredSpeed;
			Vector3D desiredVelocity = mainVelocity + correctionVelocity;

			Stuck = stuckDetector.IsStuck(currentPosition, desiredVelocity, DeltaTime);

			// 4. Smooth acceleration (PD controller)
			Vector3D velocityError = desiredVelocity - currentVelocity;
			Vector3D acceleration = velocityError * 5.0; // P coefficient

			currentVelocity += acceleration * DeltaTime;
			if(currentVelocity.LengthSquared() > maxVelocity * maxVelocity)
			{	currentVelocity = currentVelocity.Normalized() * maxVelocity;
			}
			return currentVelocity;
		}

		Vector3D FindLookaheadPoint(Vector3D pos, double distance)
		{
			// Walk along the path until distance meters are accumulated
			double accumulated = 0;
			for (int i = currentWaypointIndex; i < path.Count - 1; i++)
			{
				Vector3D segmentStart = path[i];
				Vector3D segmentEnd = path[i + 1];
				double segmentLength = (segmentEnd - segmentStart).Length();

				if (accumulated + segmentLength > distance)
				{
					// Lookahead point is within this segment
					double t = (distance - accumulated) / segmentLength;
					return Vector3D.Lerp(segmentStart, segmentEnd, t);
				}
				accumulated += segmentLength;
			}
			return path[path.Count - 1]; // End of path
		}

		Vector3D FindNearestPointOnPath(Vector3D pos)
		{
			// Find nearest point on current segment to prevent drift
			if (currentWaypointIndex >= path.Count - 1)
				return path[path.Count - 1];

			Vector3D segmentStart = path[currentWaypointIndex];
			Vector3D segmentEnd = path[currentWaypointIndex + 1];
			Vector3D segment = segmentEnd - segmentStart;
			double segmentLength = segment.Length();

			if (segmentLength < 0.001)
				return segmentStart;

			// Project position onto segment
			double t = Vector3D.Dot(pos - segmentStart, segment) / (segmentLength * segmentLength);
			t = Math.Max(0, Math.Min(1, t)); // Clamp to segment

			return segmentStart + segment * t;
		}

		double ComputeMaxSpeedForSegment(int waypointIndex)
		{
			// Slow down before sharp turns
			if (waypointIndex < path.Count - 2)
			{
				Vector3D dir1 = (path[waypointIndex + 1] - path[waypointIndex]).Normalized();
				Vector3D dir2 = (path[waypointIndex + 2] - path[waypointIndex + 1]).Normalized();
				double dot = Vector3D.Dot(dir1, dir2);

				if (dot < 0.5) return 2.5; // Sharp turn
				if (dot < 0.8) return 4.0; // Medium turn
			}
			return 10.0; // Straight section
		}
	}

	class StuckDetector
	{
		const double stuckThreshold = 15.0;
		const double minMovement = 0.5;
		Vector3D lastPosition;
		double stuckTimer = 0;

		public void Reset()
		{
			stuckTimer = 0;
		}

		public bool IsStuck(Vector3D currentPosition, Vector3D desiredVelocity, double DeltaTime)
		{
			double movement = (currentPosition - lastPosition).Length();
			lastPosition = currentPosition;

			if (movement < minMovement && desiredVelocity.Length() > 1.0)
			{
				stuckTimer += DeltaTime;
				if (stuckTimer > stuckThreshold)
				{
					stuckTimer = 0;
					return true;
				}
			}
			else
			{
				stuckTimer = 0;
			}
			return false;
		}
	}
}