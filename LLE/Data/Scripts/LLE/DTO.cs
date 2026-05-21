using System.Collections.Generic;
using ProtoBuf;
using VRageMath;

namespace LLE
{
	public class LastKnownState
	{
		public ObjectType Type;
		public long EntityId;
		public string DisplayName;
		public double X, Y, Z;
		public double LastSeenAt;
		public bool Visible;
		
		public string debug;
	}

	public class ServerCommand
	{
		public int CommandType;
		public string Payload;
	}

	public enum ObjectType { Asteroid, LargeShip, SmallShip, Floating }

	[ProtoContract]
	public class CollisionGeometry
	{
		[ProtoMember(1)] public List<CollisionShape> Shapes = new List<CollisionShape>();
		public override string ToString() => $"CollisionGeometry: {Shapes.Count} root shapes";
	}

	[ProtoInclude(10, typeof(CompoundShape))]
	[ProtoInclude(11, typeof(BoxShape))]
	[ProtoInclude(12, typeof(SphereShape))]
	[ProtoInclude(13, typeof(CapsuleShape))]
	[ProtoInclude(14, typeof(CylinderShape))]
	[ProtoInclude(15, typeof(ConvexHullShape))]
	[ProtoContract]
	public abstract class CollisionShape
	{
		[ProtoMember(1)] public Matrix Transform = Matrix.Identity;
		public abstract override string ToString();
	}
	[ProtoContract]
	public class CompoundShape : CollisionShape
	{
		[ProtoMember(1)] public List<CollisionShape> Children = new List<CollisionShape>();
		public override string ToString() => $"CompoundShape: {Children.Count} children";
	}

	[ProtoContract]
	public class BoxShape : CollisionShape
	{
		[ProtoMember(1)] public Vector3 HalfExtents;
		public override string ToString() => $"BoxShape: {HalfExtents}";
	}

	[ProtoContract]
	public class SphereShape : CollisionShape
	{
		[ProtoMember(1)] public float Radius;
		public override string ToString() => $"SphereShape: r={Radius}";
	}

	[ProtoContract]
	public class CapsuleShape : CollisionShape
	{
		[ProtoMember(1)] public Vector3 VertexA;
		[ProtoMember(2)] public Vector3 VertexB;
		[ProtoMember(3)] public float Radius;
		public override string ToString() => $"CapsuleShape: A={VertexA}, B={VertexB}, r={Radius}";
	}

	[ProtoContract]
	public class CylinderShape : CollisionShape
	{
		[ProtoMember(1)] public Vector3 VertexA;
		[ProtoMember(2)] public Vector3 VertexB;
		[ProtoMember(3)] public float Radius;
		public override string ToString() => $"CylinderShape: A={VertexA}, B={VertexB}, r={Radius}";
	}

	[ProtoContract]
	public class ConvexHullShape : CollisionShape
	{
		[ProtoMember(1)] public List<Vector3> Vertices = new List<Vector3>();
		public override string ToString() => $"ConvexHullShape: {Vertices.Count} vertices";
	}
}
