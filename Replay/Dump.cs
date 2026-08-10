using System;
using System.IO;

namespace LLE
{
	static class Dump
	{
		static void Main(string[] args)
		{
			var directory = args.Length > 0 ? args[0] : ".";

			File.WriteAllText(Path.Combine(directory, "tools.json"), Tools.Schema());
			File.WriteAllText(Path.Combine(directory, "system.txt"), Prompts.Executor);

			Console.WriteLine($"{Tools.All.Length} tools, {Tools.Schema().Length} schema chars,"
				+ $" {Prompts.Executor.Length} prompt chars");
		}
	}
}
