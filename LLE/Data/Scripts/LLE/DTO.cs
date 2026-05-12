using ProtoBuf;

namespace LLE
{
    enum MsgType { Vision, Chat, Command }

    enum ObjectType { Asteroid, LargeShip, SmallShip, Floating }

    [ProtoContract]
    class LastKnownState
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

		public string Position()
		{	return $"{X:F2} {Y:F2} {Z:F2}";
		}

		// Mod fields
		public bool Changed;
        public double LastSeenAt;
    }

    [ProtoContract]
    class ChatMessage
    {
        [ProtoMember(1)] public string Author;
        [ProtoMember(2)] public string Text;
    }

    [ProtoContract]
    class ServerCommand
    {
        [ProtoMember(1)] public int CommandType;
        [ProtoMember(2)] public string Payload;
    }
}
