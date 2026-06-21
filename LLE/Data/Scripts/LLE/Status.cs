using System.Text;

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;

namespace LLE
{
	static class Status
	{
		const double SAMPLE_INTERVAL = 2.0;

		static readonly MyDefinitionId oxygenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Oxygen");
		static readonly MyDefinitionId hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

		static readonly StringBuilder report = new StringBuilder();

		static double nextTick;

		// Previous bucket indices (0-10), -1 = not yet initialized
		static int prevHealthBucket = -1;
		static int prevOxygenBucket = -1;
		static int prevHydrogenBucket = -1;
		static int prevEnergyBucket = -1;

		public static void Initialize()
		{
			nextTick = Time.Now + SAMPLE_INTERVAL;
		}

		public static void Tick(IMyCharacter character)
		{
			if (Time.Now < nextTick) return;
			nextTick = Time.Now + SAMPLE_INTERVAL;

			float healthPct = character.Integrity * 100f;
			float oxygenPct = character.GetSuitGasFillLevel(oxygenId) * 100f;
			float hydrogenPct = character.GetSuitGasFillLevel(hydrogenId) * 100f;
			float energyPct = character.SuitEnergyLevel * 100f;

			int healthBucket = ClampBucket(healthPct);
			int oxygenBucket = ClampBucket(oxygenPct);
			int hydrogenBucket = ClampBucket(hydrogenPct);
			int energyBucket = ClampBucket(energyPct);

			if (prevHealthBucket < 0) { prevHealthBucket = healthBucket; return; }

			if (healthBucket != prevHealthBucket) AppendLine("Health", healthPct, healthBucket);
			if (oxygenBucket != prevOxygenBucket) AppendLine("Oxygen", oxygenPct, oxygenBucket);
			if (hydrogenBucket != prevHydrogenBucket) AppendLine("Hydrogen", hydrogenPct, hydrogenBucket);
			if (energyBucket != prevEnergyBucket) AppendLine("Energy", energyPct, energyBucket);

			prevHealthBucket = healthBucket;
			prevOxygenBucket = oxygenBucket;
			prevHydrogenBucket = hydrogenBucket;
			prevEnergyBucket = energyBucket;
		}

		public static string Report()
		{
			if (report.Length == 0) return null;
			string r = report.ToString();
			report.Clear();
			return r;
		}

		public static void ReportNow(IMyCharacter character)
		{
			float healthPct = character.Integrity * 100f;
			float oxygenPct = character.GetSuitGasFillLevel(oxygenId) * 100f;
			float hydrogenPct = character.GetSuitGasFillLevel(hydrogenId) * 100f;
			float energyPct = character.SuitEnergyLevel * 100f;

			report.Append($"* Health: {healthPct:F0}%\n");
			report.Append($"* Oxygen: {oxygenPct:F0}%\n");
			report.Append($"* Hydrogen: {hydrogenPct:F0}%\n");
			report.Append($"* Energy: {energyPct:F0}%\n");
		}

		static int ClampBucket(float pct)
		{
			int bucket = (int)(pct / 10f);
			if (bucket < 0) return 0;
			if (bucket > 10) return 10;
			return bucket;
		}

		static void AppendLine(string label, float pct, int bucket)
		{
			report.Append($"* {label}: {pct:F0}%\n");
		}
	}
}
