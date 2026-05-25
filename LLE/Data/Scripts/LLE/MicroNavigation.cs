using System;
using System.Collections.Generic;
using VRageMath;

namespace LLE {

class MicroNavigation {
	
	private const double lookaheadDistance = 1.0;

	private List<Vector3D> path;
	private int currentWaypointIndex;

	public void Run(List<Vector3D> path)
	{	this.path = path;
		currentWaypointIndex = 0;
	}
	
	public Vector3D ComputeDesiredVelocity(Vector3D currentPosition, Vector3D currentVelocity, double DeltaTime = 1.0 / 60)
	{
		if (currentWaypointIndex >= path.Count) return Vector3D.Zero; // Arrived
		
		// 1. Find the lookahead point on the path ahead
		Vector3D targetPoint = FindLookaheadPoint(currentPosition, lookaheadDistance);
		
		// 2. Compute desired velocity (Seek + Arrive)
		Vector3D toTarget = targetPoint - currentPosition;
		double distance = toTarget.Length();
		
		if (distance < 0.5) {
			// Close to waypoint, switch to next
			currentWaypointIndex++;
			return ComputeDesiredVelocity(currentPosition, currentVelocity);
		}
		
		// Maximum speed depends on distance (slow down before turns)
		double maxSpeed = ComputeMaxSpeedForSegment(currentWaypointIndex);
		double desiredSpeed = Math.Min(maxSpeed, distance * 2.0); // Arrive behavior
		
		Vector3D desiredVelocity = toTarget.Normalized() * desiredSpeed;
		
		// 3. Smooth acceleration (PD controller)
		Vector3D velocityError = desiredVelocity - currentVelocity;
		Vector3D acceleration = velocityError * 5.0; // P coefficient
		
		return currentVelocity + acceleration * DeltaTime;
	}
	
	Vector3D FindLookaheadPoint(Vector3D pos, double distance) {
		// Walk along the path until distance meters are accumulated
		double accumulated = 0;
		for (int i = currentWaypointIndex; i < path.Count - 1; i++) {
			Vector3D segmentStart = path[i];
			Vector3D segmentEnd = path[i+1];
			double segmentLength = (segmentEnd - segmentStart).Length();
			
			if (accumulated + segmentLength > distance) {
				// Lookahead point is within this segment
				double t = (distance - accumulated) / segmentLength;
				return Vector3D.Lerp(segmentStart, segmentEnd, t);
			}
			accumulated += segmentLength;
		}
		return path[path.Count - 1]; // End of path
	}
	
	double ComputeMaxSpeedForSegment(int waypointIndex) {
		// Slow down before sharp turns
		if (waypointIndex < path.Count - 2) {
			Vector3D dir1 = (path[waypointIndex+1] - path[waypointIndex]).Normalized();
			Vector3D dir2 = (path[waypointIndex+2] - path[waypointIndex+1]).Normalized();
			double dot = Vector3D.Dot(dir1, dir2);
			
			if (dot < 0.5) return 2.0; // Sharp turn
			if (dot < 0.8) return 4.0; // Medium turn
		}
		return 8.0; // Straight section
	}
}
}
