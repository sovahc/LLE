using System.Collections.Generic;
using System.Text;

using VRageMath;

namespace LLE
{
	class LoopDetector
	{
		private string lastBatch;

		// Returns true if the command batch is identical to the previous one.
		public bool IsLoop(List<string> commands)
		{
			var current = string.Join("\n", commands);
			if (current == lastBatch)
				return true;
			lastBatch = current;
			return false;
		}

		public void Reset()
		{	lastBatch = null;
		}
	}

	class LLM
	{
		private void Log(string s) => LLE.Log(s);

		private readonly StringBuilder Reasoning = new StringBuilder(); // input
		private readonly StringBuilder Content = new StringBuilder(); // input
		private readonly StringBuilder contentToProcess = new StringBuilder();
		private readonly StringBuilder output = new StringBuilder();

		private MessageType lastType = MessageType.Stop;
		private bool waitingForResponse;
		public static bool pause;

		private Commands commands;

		private Queue<string> batch = new Queue<string>();

		private readonly LoopDetector loopDetector = new LoopDetector();

		public void ResetLoopDetector()
		{	loopDetector.Reset();
		}

		public LLM(Commands commands_)
		{	commands = commands_;
		}

		public void OnCommandFinished(CommandResult result)
		{
			var currentCommand = batch.Dequeue();
			
			var tag = result.Ok ? "OK" : "FAILED";
			Append($"→ {currentCommand}: [{tag}] {result.Message}\n", Color.Cornsilk);

			if(!result.Ok && batch.Count > 0)
			{	Append($"Remaining {batch.Count} command(s) ignored: {string.Join("; ", batch)}\n", Color.Cornsilk);
				batch.Clear();
				return;
			}

			RunNextPending();
		}

		private void RunNextPending()
		{
			if (batch.Count == 0) return;

			var result = commands.Execute(batch.Peek());

			if (result != null)
			{
				// Synchronous command — continue immediately
				OnCommandFinished(result);
			}
		}

		public void Append(string text, Color consoleColor)
		{	MyConsole.AddMultiline(text, consoleColor);
			output.Append(text);
		}

		public void Tick()
		{
			PollNewChunksFromLLM();
				// stores data to contentToProcess

			var ec = commands.GetEngineerCenter();

			Vision.Tick(ec);
			string vr = Vision.VisionReport(ec);
			if(vr != null)
			{	Append("[VISION]:\n", Color.Yellow);
				Append(vr, Color.Yellow);
				pause = false;
			}

			// Status subsystem reports
			commands.Status_Tick();
			string sr = commands.Status_ReportChanged();
			if(sr != null)
			{	Append("[STATUS]:", Color.Azure);
				Append(sr, Color.Azure);
				Append("\n", Color.Azure);
				pause = false;
			}
			
			if (batch.Count != 0) // We have running commands
			{	var result = commands.Update();
				if (result != null)
					OnCommandFinished(result);

				return;
			}

			// batch.Count == 0

			if(commands.InProgress()) // manual command from chat
			{	var result = commands.Update();
				if (result != null)
				{	MyConsole.AddMultiline("=", Color.Red);
					MyConsole.AddMultiline(result.Message, Color.Magenta);
					MyConsole.AddMultiline("\n", Color.Magenta);
				}
				return;
			}

			if(waitingForResponse) return;
			if(pause) return;

			if (output.Length != 0) // We have data for LLM
			{
				// Send accumulated results to LLM
				Log($"toLLM: {output}");
				LLE_Loader.SendMessageToLLM(output.ToString());
				output.Clear();
				waitingForResponse = true;
				return;
			}

			// Only accept new commands if everything is complete

			var ctp = contentToProcess;

			if(ctp.Length != 0)
			{	ProcessLlmContent(ctp.ToString());
				ctp.Clear();
			}
		}

		private void ContextStatisitic()
		{
			int used, total;
			LLE_Loader.GetContextStatus(out used, out total);
			if (total > 0)
			{	int percent = used * 100 / total;
				MyConsole.Add($"[CONTEXT] {used}/{total} chars ({percent}%)", Color.LightPink);
			}
		}

		private void PollNewChunksFromLLM()
		{
			for (int i = 0; i < 10; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(out m)) return;

				// Type changed — log and clear the old buffer
				if (m.Type != lastType)
				{
					switch (lastType)
					{
						case MessageType.Reasoning:
							Log($"llmReasoning:\n{Reasoning}");
							Reasoning.Clear();
							break;
						case MessageType.Content:
							contentToProcess.Append(Content);
							contentToProcess.Append("\n");

							Log($"llmContent:\n{Content}");
							Content.Clear();
							break;
					}

				}
				lastType = m.Type;

				switch(m.Type)
				{	case MessageType.Reasoning:
						MyConsole.AddMultiline(m.Payload, Color.LightGray);
						Reasoning.Append(m.Payload);
						break;
					case MessageType.Content:
						MyConsole.AddMultiline(m.Payload, Color.Cyan);
						Content.Append(m.Payload);
						break;
					case MessageType.Stop:
						MyConsole.AddMultiline("\n", Color.White);
						
						waitingForResponse = false;
						ContextStatisitic();

						return;
				
					case MessageType.Error:
						MyConsole.AddMultiline("\n[LLM ERROR] " + m.Payload + "\n", Color.Red);
						
						waitingForResponse = false;
						pause = true;

						return;
				}
			}
		}

		private void ProcessLlmContent(string content)
		{
			content = content.Trim();
			const string prefix = "Execute `";

			var lines = content.Split('\n');
			List<string> cc = new List<string>();

			for (int i = lines.Length - 1; i >= 0; --i)
			{
				string l = lines[i].Trim();
				if (!l.StartsWith(prefix)) break;

				int closingBacktick = l.IndexOf('`', prefix.Length);
				if (closingBacktick < 0)
				{
					Log($"ProcessLlmContent ERROR: Missing closing backtick in line: {l}");
					break;
				}

				cc.Add(l.Substring(prefix.Length, closingBacktick - prefix.Length));
			}

			// Reverse back to original order (first command first)
			cc.Reverse();

			if (cc.Count == 0)
			{
				Append($"!ERROR: No commands found in your last message:\n---\n{content}\n---\n"
					+ "Use 'Execute `command`' on separate lines.\n", Color.Red);
				return;
			}

			if(cc.Count == 1 && 0 == string.Compare(cc[0], "pause", true))
			{}
			else if (loopDetector.IsLoop(cc))
			{
				Append($"!ERROR: LOOP DETECTED. Your last message was:\n---\n{content}\n---\n"
					+ "This is identical to the previous command batch. If the task is complete, use `pause`.\n"
					+ "Otherwise, try a different approach.\n", Color.Red);
				return;
			}

			// Queue commands and start execution
			foreach (var c in cc) batch.Enqueue(c);

			RunNextPending();
		}
	}
}
