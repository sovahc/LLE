using ProtoBuf;

namespace LLE
{
    [ProtoContract]
    public class LastKnownState
    {
        [ProtoMember(1)]
        public ObjectType Type;
        [ProtoMember(2)]
        public string DisplayName;
        [ProtoMember(3)]
        public double X;
        [ProtoMember(4)]
        public double Y;
        [ProtoMember(5)]
        public double Z;

        public double LastSeenAt;
    }

    [ProtoContract]
    public class ServerCommand
    {
        [ProtoMember(1)] public int CommandType;
        [ProtoMember(2)] public string Payload;
    }

    public enum ObjectType { Asteroid, LargeShip, SmallShip, Floating }
}
