using System.Text;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;

namespace LLE
{
	static class Status
	{
		private static readonly MyDefinitionId oxygenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Oxygen");
		private static readonly MyDefinitionId hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

		private static readonly StringBuilder report = new StringBuilder();
		private static double nextReport;

		private struct All
		{	public float Health;
			public float Energy;
			public float Oxygen;
			public float Hydrogen;
		}

		private static All MinusOne()
		{	return new All() { Health = -1, Energy = -1, Oxygen = -1, Hydrogen = -1  };
		}

		private static All previous = MinusOne();

		private static int Bucket(float f)
		{	if(f < 0) return int.MinValue;
			if(f > 1) return int.MaxValue;
			if(f < 0.05) return 1;
			if(f < 0.10) return 2;
			if(f < 0.15) return 3;
			if(f < 0.20) return 4;
			if(f < 0.25) return 5;
			if(f < 0.50) return 6;
			if(f < 0.75) return 7;
			return 10;
		}

		public static void Initialize()
		{
			nextReport = Time.Now + 1;
		}

		public static void Tick(IMyCharacter character)
		{
			if (Time.Now < nextReport) return;
			nextReport = Time.Now + 2.5;

			var current = new All()
			{	Health = character.Integrity/100,
				Energy = character.SuitEnergyLevel,
				Oxygen = character.GetSuitGasFillLevel(oxygenId),
				Hydrogen = character.GetSuitGasFillLevel(hydrogenId)
			};

			if (Bucket(current.Health) != Bucket(previous.Health)) report.Append($" Health {current.Health*100:F0}%");
			if (Bucket(current.Energy) != Bucket(previous.Energy)) report.Append($" Energy {current.Energy*100:F0}%");
			if (Bucket(current.Oxygen) != Bucket(previous.Oxygen)) report.Append($" Oxygen {current.Oxygen*100:F0}%");
			if (Bucket(current.Hydrogen) != Bucket(previous.Hydrogen)) report.Append($" Hydrogen {current.Hydrogen*100:F0}%");

			previous = current;
		}

		public static string Report()
		{
			if (report.Length == 0) return null;
			string r = report.ToString();
			report.Clear();
			return r;
		}

		public static string ReportNow(IMyCharacter character)
		{
			previous = MinusOne();
			nextReport = Time.Now - 1;
			Tick(character);

			var r = report.ToString();
			report.Clear();
			return r;
		}
	}
}
