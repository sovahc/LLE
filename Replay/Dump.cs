using System;
using System.IO;

namespace VRageMath
{
	// ToolCall needs it for i j k and nothing else; the game's own type never reaches this project.
	struct Vector3I
	{
		public int X, Y, Z;
		public Vector3I(int x, int y, int z) { X = x; Y = y; Z = z; }
		public static readonly Vector3I Zero = new Vector3I(0, 0, 0);
		public override string ToString() => $"{X} {Y} {Z}";
	}
}

namespace LLE
{
	static class Dump
	{
		private const string PutCall =
			"{\"i\":-1,\"j\":1,\"k\":-7,\"items\":[{\"item\":\"Steel Plate\",\"count\":70},"
			+ "{\"item\":\"Construction Comp.\"},{\"item\":\"Motor\",\"count\":24}]}";

		static void Main(string[] args)
		{
			var directory = args.Length > 0 ? args[0] : ".";

			File.WriteAllText(Path.Combine(directory, "tools.json"), Tools.Schema());
			File.WriteAllText(Path.Combine(directory, "system.txt"), Prompts.Executor);

			Console.WriteLine($"{Tools.All.Length} tools, {Tools.Schema().Length} schema chars,"
				+ $" {Prompts.Executor.Length} prompt chars");

			string error;
			var call = ToolCall.Parse("put", PutCall, out error);

			if (call == null)
			{	Console.WriteLine("put did not parse: " + error);
				return;
			}

			Console.WriteLine(call.Text);

			VRageMath.Vector3I ijk;
			Console.WriteLine($"ijk parsed: {call.Ijk(out ijk)} {ijk}");

			foreach (var entry in call.List("items"))
			{	double count;
				bool has = entry.Number("count", out count);
				Console.WriteLine($"  {entry.Str("item")} -> {(has ? count.ToString() : "all")}");
			}
		}
	}
}
