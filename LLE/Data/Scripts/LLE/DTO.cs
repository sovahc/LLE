using System.Dynamic;
using ProtoBuf;

namespace LLE
{
    [ProtoContract]
    class LastKnownState
    {
        [ProtoMember(1)]
        public string DisplayName;
        [ProtoMember(2)]
        public double X;
        [ProtoMember(3)]
        public double Y;
        [ProtoMember(4)]
        public double Z;

		public string Position()
		{	return $"{X:F2} {Y:F2} {Z:F2}";
		}
    }
}
