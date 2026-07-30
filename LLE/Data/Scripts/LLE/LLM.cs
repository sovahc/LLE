using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using VRageMath;

namespace LLE
{
	class LoopDetector
	{
		private string lastBatch;
		private int repeats; // consecutive times lastBatch was seen

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

			if (commands.Count == 0)
			{	message = "!ERROR: No commands found in your last message."
					+ " Wrap your commands in <execute>...</execute>.\n";
				return repeats >= 4;
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
					+ " Put `pause` inside <execute> to stop.\n";
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
		public static int contextWarnStage;

		private Commands commands;

		private Queue<string> batch = new Queue<string>();

		private double commandStartTime;

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

			string took = "";
			double elapsed = Time.Now - commandStartTime;
			if(elapsed >= 0.1)
				took = $" (Took {elapsed:F1}s)";
			commandStartTime = 0;

			Append($"→ {currentCommand}: [{tag}] {result.Message}{took}\n", Color.Cornsilk);

			if(result.Status != CommandStatus.Success && batch.Count > 0)
			{	Append($"Remaining {batch.Count} command(s) ignored: {string.Join("; ", batch)}\n", Color.Cornsilk);
				batch.Clear();
				return;
			}

			// Next command (if any) is driven by Tick() — one per tick.
		}

		private void RunNextPending()
		{
			if (batch.Count == 0) return;

			commandStartTime = Time.Now;

			var result = commands.Execute(batch.Peek());

			// Execute() returns null only when it pushed a coroutine. If it didn't, the head
			// would never be dequeued and Tick() would re-run this command every tick.
			if (result == null && !commands.InProgress())
				result = $"Internal error: command '{batch.Peek()}' produced no result.";

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
			
			if (batch.Count != 0)
			{
				if(commands.InProgress())   // coroutine command — step it
				{	var result = commands.Update();
					if (result != null)
						OnCommandFinished(result);
				}
				else                        // instant command — one per tick
					RunNextPending();

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
				int used, total;
				LLE_Loader.GetContextStatus(out used, out total);

				if (used + 2500 > total)
				{
					LLE_Loader.RestartContext();
					commands.SetSystemPromptAndMemory();
					Append("[CONTEXT AUTO-RESET — context was full]\n", Color.Red);
					contextWarnStage = 0;
				}
				else
				{	int pct = used * 100 / total;

					// Escalating warnings: a weak model ignores a single polite notice,
					// so each stage gets louder and the last one is an order.
					int stage = pct >= 90 ? 3 : pct >= 80 ? 2 : pct >= 70 ? 1 : 0;

					if(stage > contextWarnStage)
					{	contextWarnStage = stage;

						if(stage == 1)
							Append($"!Warning: Context is {pct}% full."
								+ " Save anything you must not forget: memory 'key' 'value'\n", Color.Yellow);
						else if(stage == 2)
							Append($"!WARNING: Context is {pct}% full."
								+ " Save your state now: memory 'key' 'value'"
								+ ", then reset it yourself: restart\n", Color.Yellow);
						else
							Append($"!ERROR: Context is {pct}% full and will be wiped automatically very soon."
								+ " You must save your state with memory 'key' 'value' and then issue restart."
								+ " Everything not in memory will be lost.\n", Color.Red);
					}
				}

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

						if(contentToProcess.Length == 0)
							contentToProcess.Append("[EMPTY RESPONSE]");
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

			// The bot's own words go back into the transcript. Without them it reads a list of
			// results with no record of what it said or meant. Reasoning stays out: the chat
			// template drops thought from previous turns anyway.
			// Straight into output, not through Append — the console printed it while streaming.
			output.Append("[YOU]:\n").Append(content).Append("\n");

			const string openTag  = "<execute>";
			const string closeTag = "</execute>";

			// Exactly one <execute> block is allowed; multiple blocks are rejected (not "keep the last").
			int count = 0, scan = 0;
			while (true)
			{	int at = content.IndexOf(openTag, scan, StringComparison.OrdinalIgnoreCase);
				if (at < 0) break;
				count++;
				scan = at + openTag.Length;
			}
			if (count == 0)
			{	Append("[ERROR] No <execute> block found. Wrap your commands in <execute>...</execute>.\n", Color.Red);
				return;
			}
			if (count > 1)
			{	Append("[ERROR] Found " + count + " <execute> blocks; exactly one is allowed. No commands were executed. Put all commands into a single <execute> block.\n", Color.Red);
				return;
			}

			int start = content.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
			start += openTag.Length;

			int end = content.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
			if (end < 0) end = content.Length;

			var lines = content.Substring(start, end - start).Split('\n');
			List<string> cc = new List<string>();
			foreach (var line in lines)
			{	string l = line.Trim();
				if (l.Length == 0) continue;
				// Strip optional "Execute " prefix for backward compat
				if (l.StartsWith("Execute ", StringComparison.OrdinalIgnoreCase))
					l = l.Substring(8);
				cc.Add(MyTrim(l));
			}

			if (cc.Count == 0)
			{	Append("[ERROR] No commands found inside <execute> block.\n", Color.Red);
				return;
			}

			// Control commands (pause, restart) must be issued alone
			bool hasControl = cc.Any(c => c.Equals("restart", StringComparison.OrdinalIgnoreCase)
				                       || c.Equals("pause", StringComparison.OrdinalIgnoreCase));
			if (hasControl && cc.Count > 1)
			{
				Append("[ERROR] 'pause' and 'restart' must be used alone, not mixed with other commands.\n", Color.Red);
				return;
			}

			if (cc[0].Equals("restart", StringComparison.OrdinalIgnoreCase))
			{	LLE_Loader.RestartContext();
				commands.SetSystemPromptAndMemory();
				Append("[CONTEXT RESET]\n", Color.LightGreen);
				loopDetector.Reset();
				return;
			}

			// pause is exempt from loop detection
			if (!cc[0].Equals("pause", StringComparison.OrdinalIgnoreCase))
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
