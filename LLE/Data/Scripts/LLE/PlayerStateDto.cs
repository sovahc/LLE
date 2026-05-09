using ProtoBuf;

namespace LLE
{
	[ProtoContract]
	public class PlayerStateDto
	{
		[ProtoMember(1)]
		public double PositionX;

		[ProtoMember(2)]
		public double PositionY;

		[ProtoMember(3)]
		public double PositionZ;
	}
}
