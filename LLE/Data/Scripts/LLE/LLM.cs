using System.Collections.Generic;
using System.Text;

using VRageMath;

namespace LLE
{
	class LoopDetector
	{
		private string lastBatch;
		private int repeats;            // consecutive times lastBatch was seen

		// Returns true to BLOCK execution. On a non-blocking detection, 'message' carries a warning to append.
		public bool IsLoop(List<string> commands, out string message)
		{
			var current = string.Join("\n", commands);
			if (current == lastBatch)
				repeats++;
			else
			{	lastBatch = current;
				repeats = 1;
			}

			if (repeats == 1)
			{	message = null;
				return false;
			}
			if (repeats == 2)
			{	message = "!Warning: This command batch is identical to the previous one."
					+ " If the task is complete, use `pause`.\n";
				return false;
			}
			if (repeats == 3)
			{	message = "!WARNING: Identical batch repeated again."
					+ " Output \"Execute `pause`\" to stop.\n";
				return false;
			}
			// repeats >= 4
			{	message = "!ERROR: LOOP DETECTED. This command batch has been repeated too many times and is blocked.\n";
				return true;
			}
		}

		public void Reset()
		{	lastBatch = null;
			repeats = 0;
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

			string tag;
			switch(result.Status)
			{	case CommandStatus.Success: tag = "OK"; break;
				case CommandStatus.Incomplete: tag = "INCOMPLETE"; break;
				case CommandStatus.Error: tag = "FAILED"; break;
				default: tag = "???"; break;
			}
			Append($"→ {currentCommand}: [{tag}] {result.Message}\n", Color.Cornsilk);

			if(result.Status != CommandStatus.Success && batch.Count > 0)
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

		private void ContextStatistic()
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
						ContextStatistic();

						return;
				
					case MessageType.Error:
						MyConsole.AddMultiline("\n[LLM ERROR] " + m.Payload + "\n", Color.Red);
						
						waitingForResponse = false;
						pause = true;

						return;
				}
			}
		}

		private string MyTrim(string s)
		{	if(s.Length < 2) return s;
			char fc = s[0];
			if ((fc == '`' || fc == '\'' || fc == '\"') && s[s.Length - 1] == fc) return s.Substring(1, s.Length - 2);
			return s;
		}

		private void ProcessLlmContent(string content)
		{
			content = content.Trim();
			const string prefix = "Execute ";

			var lines = content.Split('\n');
			List<string> cc = new List<string>();

			for (int i = lines.Length - 1; i >= 0; --i)
			{
				string l = lines[i].Trim();
				if (!l.StartsWith(prefix)) break;

				var command = MyTrim(l.Substring(prefix.Length));

				cc.Add(command);
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
			else
			{
				string loopMsg;
				bool blocked = loopDetector.IsLoop(cc, out loopMsg);
				if (loopMsg != null)
					Append(loopMsg, blocked ? Color.Red : Color.Yellow);
				if (blocked)
				{	Append($"Your last message was:\n---\n{content}\n---\n", Color.Red);
					return;
				}
			}

			// Queue commands and start execution
			foreach (var c in cc) batch.Enqueue(c);

			RunNextPending();
		}
	}
}
