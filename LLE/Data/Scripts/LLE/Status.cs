using System.Text;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Character.Components;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;

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
		{	character = character_;
			nextReport = Time.Now + 1;
		}

		private struct All
		{	public float Health;
			public float Energy;
			public float Hydrogen;
		}

		private static All Undefined()
		{	return new All() { Health = -1, Energy = -1, Hydrogen = -1  };
		}

		private All previous = Undefined();

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

			var sc = character.Components?.Get<MyCharacterStatComponent>();
			if (sc?.Food != null) sc.Food.Value = sc.Food.MaxValue;

			var oc = character.Components?.Get<MyCharacterOxygenComponent>();
			if (oc != null) oc.SuitOxygenLevel = 1f;

			MakeReport();
		}

		private void MakeReport()
		{
			var current = new All()
			{
				Health = character.Integrity / 100,
				Energy = character.SuitEnergyLevel,
				Hydrogen = character.GetSuitGasFillLevel(hydrogenId)
			};

			if (Bucket(current.Health) != Bucket(previous.Health)) report.Append($" Health {current.Health * 100:F0}%");
			if (Bucket(current.Energy) != Bucket(previous.Energy)) report.Append($" Energy {current.Energy * 100:F0}%");
			if (Bucket(current.Hydrogen) != Bucket(previous.Hydrogen)) report.Append($" Hydrogen {current.Hydrogen * 100:F0}%");

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
