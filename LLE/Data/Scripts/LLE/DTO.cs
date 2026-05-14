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
}
