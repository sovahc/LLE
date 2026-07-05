using System.Linq;
using System.Text;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Character.Components;

namespace LLE
{
	class Status
	{
		private static readonly MyDefinitionId hydrogenId =
			new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

		private readonly IMyCharacter character;
		private double nextReport;
		private readonly StringBuilder report = new StringBuilder();

		public Status(IMyCharacter character_)
		{
			character = character_;
			nextReport = Time.Now + 1;
		}

		public struct Charge
		{	public float Health;
			public float Energy;
			public float Hydrogen;
		}

		private static Charge Undefined()
		{	return new Charge() { Health = -1, Energy = -1, Hydrogen = -1  };
		}

		private Charge previous = Undefined();

		private int Bucket(float f)
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

		public void Tick()
		{
			if (Time.Now < nextReport) return;
			nextReport = Time.Now + 2.5;

			Set_food_and_oxygen_to_maximum();

			MakeReport();
		}

		private void Set_food_and_oxygen_to_maximum()
		{
			var sc = character.Components?.Get<MyCharacterStatComponent>();
			if (sc?.Food != null) sc.Food.Value = sc.Food.MaxValue;

			var oc = character.Components?.Get<MyCharacterOxygenComponent>();
			if (oc != null) oc.SuitOxygenLevel = 1f;
		}

		public Charge Current()
		{
			var sc = character.Components?.Get<MyCharacterStatComponent>();
			var healthMax = sc.Health.MaxValue;
			float health = sc.Health.Value / healthMax;

			return new Charge()
			{
				Health = health,
				Energy = character.SuitEnergyLevel,
				Hydrogen = character.GetSuitGasFillLevel(hydrogenId)
			};
		}

		public Charge Maximal()
		{
			var sc = character.Components?.Get<MyCharacterStatComponent>();
			var healthMax = sc.Health.MaxValue;
			var hydrogenMax = (character.Definition as MyCharacterDefinition)?.SuitResourceStorage?
				.FirstOrDefault(g => g.Id.SubtypeName == hydrogenId.SubtypeName)?
				.MaxCapacity ?? 0f;

			return new Charge()
			{	Health = healthMax,
				Energy = Constants.CharacterBatteryWh,
				Hydrogen = hydrogenMax,
			};
		}

		public static bool IsCharging(Charge previous, Charge current)
		{	return current.Health > previous.Health ||
					current.Energy > previous.Energy ||
					current.Hydrogen > previous.Hydrogen;
		}

		private void MakeReport()
		{
			var current = Current();
			var m = Maximal();

			var c = current;
			var p = previous;

			if (Bucket(c.Health) != Bucket(p.Health)) report.Append($" Health {c.Health * 100:F0}% ({c.Health * m.Health:F0})");
			if (Bucket(c.Energy) != Bucket(p.Energy)) report.Append($" Energy {c.Energy * 100:F0}% ({c.Energy * m.Energy:F1}Wh)");
			if (Bucket(c.Hydrogen) != Bucket(p.Hydrogen)) report.Append($" Hydrogen {c.Hydrogen * 100:F0}% ({c.Hydrogen * m.Hydrogen:F0}L)");

			previous = current;
		}

		public string ReportChanged()
		{
			if (report.Length == 0) return null;
			string r = report.ToString();
			report.Clear();
			return r;
		}

		public string ReportAll()
		{
			previous = Undefined();
			report.Clear();
			MakeReport();

			var r = report.ToString();
			report.Clear();
			return r;
		}
	}
}
