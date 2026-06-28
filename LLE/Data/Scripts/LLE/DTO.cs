using System.Collections.Generic;

using VRageMath;
using ProtoBuf;

namespace LLE
{
	public enum ObjectType { Unknown, Asteroid, LargeShip, SmallShip, Floating }

	public class LastKnownState
	{
		public ObjectType Type;
		public long EntityId;
		public string DisplayName;
		public double X, Y, Z;
		public double LastSeenAt;
		public bool Visible;
		public bool Closed;
		public bool Report;
	}

	//

	public enum MessageType { Reasoning, Content, Stop }

	public class FromLLM
	{
		public MessageType Type;
		public string Payload;
	}

	//

	[ProtoContract]
	public struct DefinitionIdAsText
	{
		[ProtoMember(1)] public string TypeId;
		[ProtoMember(2)] public string SubtypeId;
	}

	[ProtoContract]
	public class CollisionGeometry
	{
		[ProtoMember(1)] public List<CollisionShape> Shapes = new List<CollisionShape>();
		[ProtoMember(2)] public List<DetectorInfo> Detectors = new List<DetectorInfo>();
		public override string ToString() => $"CollisionGeometry: {Shapes.Count} root shapes, {Detectors.Count} detectors";
	}

	public struct Parallelogram
	{	public Vector3 A, B, C; // D = A + AB + AC
		public Parallelogram(Vector3 a, Vector3 b, Vector3 c)
		{	A = a; B = b; C = c;
		}
	}

	[ProtoContract]
	public class DetectorInfo
	{
		[ProtoMember(1)] public string Name;
		[ProtoMember(2)] public Matrix Transform;
		public override string ToString() => $"Detector: {Name}";

		public List<Parallelogram> ForRaycast;
	}

	[ProtoInclude(11, typeof(BoxShape))]
	[ProtoInclude(12, typeof(SphereShape))]
	[ProtoInclude(13, typeof(CapsuleShape))]
	[ProtoInclude(14, typeof(CylinderShape))]
	[ProtoInclude(15, typeof(ConvexHullShape))]
	[ProtoContract]
	public class CollisionShape
	{
		public CollisionShape() { }
		[ProtoMember(1)] public Matrix Transform = Matrix.Identity;
		public override string ToString() => "CollisionShape";
	}

	[ProtoContract]
	public class BoxShape : CollisionShape
	{
		public BoxShape() { }
		[ProtoMember(2)] public Vector3 HalfExtents;
		public override string ToString() => $"BoxShape: {HalfExtents}";
	}

	[ProtoContract]
	public class SphereShape : CollisionShape
	{
		public SphereShape() { }
		public Vector3 Center;
		[ProtoMember(2)] public float Radius;
		public override string ToString() => $"SphereShape: r={Radius}";
	}

	[ProtoContract]
	public class CapsuleShape : CollisionShape
	{
		public CapsuleShape() { }
		[ProtoMember(2)] public Vector3 VertexA;
		[ProtoMember(3)] public Vector3 VertexB;
		[ProtoMember(4)] public float Radius;
		public override string ToString() => $"CapsuleShape: A={VertexA}, B={VertexB}, r={Radius}";
	}

	[ProtoContract]
	public class CylinderShape : CollisionShape
	{
		public CylinderShape() { }
		[ProtoMember(2)] public Vector3 VertexA;
		[ProtoMember(3)] public Vector3 VertexB;
		[ProtoMember(4)] public float Radius;
		public override string ToString() => $"CylinderShape: A={VertexA}, B={VertexB}, r={Radius}";
	}

	[ProtoContract]
	public class ConvexHullShape : CollisionShape
	{
		public ConvexHullShape() { }
		[ProtoMember(2)] public List<Vector3> Vertices = new List<Vector3>();
		public override string ToString() => $"ConvexHullShape: {Vertices.Count} vertices";
	}
}
