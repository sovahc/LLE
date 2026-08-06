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
		public const string ExecuteOpenTag  = "<execute>";
		public const string ExecuteCloseTag = "</execute>";
		public const string StopWorld = "[YOU]"; // don't hallucinate, please.
			// The model may keep generating past </execute> (hallucinated command results,
			// [VISION]/[YOU] lines, a second reasoning + a second <execute>)

		private void Log(string s) => LLE.Log(s);

		// The conversation lives here, not in the loader: the loader is transport and must not
		// hold state that belongs to a turn.
		private readonly List<string> transcript = new List<string>();
		private int transcriptChars;
		private int turn;

		private readonly LlmChannel channel = new LlmChannel(LlmChannel.Executor) { EchoToConsole = true };
		private readonly Verifier verifier = new Verifier();

		private string pendingResponse; // a finished answer waiting for a free moment to be run

		[Flags]
		public enum Destination : byte
		{	None    = 0,
			Console = 1,
			Log     = 2,
			LLM     = 4,
			All     = Console | Log | LLM,
		}

		private readonly StringBuilder output = new StringBuilder();
		private readonly StringBuilder logBuf  = new StringBuilder();

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

		public void Append(string text, Color consoleColor, Destination d = Destination.All)
		{	if((d & Destination.Console) != 0) MyConsole.AddMultiline(text, consoleColor);
			if((d & Destination.Log)     != 0) logBuf.Append(text);
			if((d & Destination.LLM)     != 0) output.Append(text);
		}

		public void Tick()
		{
			PollExecutorChannel();
				// stores data to pendingResponse

			verifier.Tick(turn);

			var ec = commands.GetEngineerCenter();

			Vision.Tick(ec);
			string vr = Vision.VisionReport(ec);
			if(vr != null)
			{	Append("[VISION]:\n", Color.Yellow);
				Append(vr, Color.Yellow);
				pause = false;
			}

			commands.Draft_Tick();

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

			if(channel.Busy) return;
			if(pause) return;

			// Process a finished response BEFORE any send. Otherwise an async sensor
			// report (VISION/STATUS) can fire the send below and orphan it; the next
			// response then appends onto it ("Found 2 <execute> blocks").
			if(pendingResponse != null)
			{	var response = pendingResponse;
				pendingResponse = null;
				ProcessLlmContent(response);
				if(batch.Count != 0) return;   // commands enqueued — execute before talking to LLM
				if(pause) return;              // response was pause/restart — do not send this turn
			}

			if (output.Length != 0) // We have data for LLM
			{
				int used = ContextUsed;
				int total = channel.ContextWindow;

				if (total <= 0)
				{	// No such channel in the loader config — there is nobody to talk to.
					output.Clear();
					logBuf.Clear();
					return;
				}

				if (used + 2500 > total)
				{
					ClearTranscript();
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
				Log($"toLLM: {logBuf}");

				var message = output.ToString();
				transcript.Add(message);
				transcriptChars += message.Length;
				output.Clear();
				logBuf.Clear();

				var conversation = "\n" + string.Join("\n", transcript);
				channel.Send(conversation);
				turn++;

				// The verifier reads the same text the bot is answering right now. It is one call
				// per turn and it blocks nothing: if it is still busy, this turn goes unchecked.
				verifier.Kick(conversation, turn);
				return;
			}

		}

		private int ContextUsed
		{	get { return commands.SystemPromptChars + transcriptChars; }
		}

		private void ClearTranscript()
		{	transcript.Clear();
			transcriptChars = 0;
		}

		private void ContextStatistic()
		{
			int total = channel.ContextWindow;
			if (total <= 0) return;
			int used = ContextUsed;
			MyConsole.Add($"[CONTEXT] {used}/{total} chars ({used * 100 / total}%)", Color.LightPink);
		}

		private void PollExecutorChannel()
		{
			string payload;
			switch (channel.Poll(out payload))
			{
				case ChannelEvent.Response:
					pendingResponse = payload;
					ContextStatistic();
					break;

				case ChannelEvent.Error:
					MyConsole.AddMultiline("\n[LLM ERROR] " + payload + "\n", Color.Red);
					pause = true;
					break;
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
			// Console already printed it while streaming; llmContent already logged it.
			Append($"[YOU]:\n{content}\n", Color.Cyan, Destination.LLM);
			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);

			// Exactly one <execute> block is allowed; multiple blocks are rejected (not "keep the last").
			int count = 0, scan = 0;
			while (true)
			{	int at = content.IndexOf(ExecuteOpenTag, scan, StringComparison.OrdinalIgnoreCase);
				if (at < 0) break;
				count++;
				scan = at + ExecuteOpenTag.Length;
			}
			if (count == 0)
			{	Append("[ERROR] No <execute> block found. Wrap your commands in <execute>...</execute>.\n", Color.Red);
				return;
			}
			if (count > 1)
			{	Append("[ERROR] Found " + count + " <execute> blocks; exactly one is allowed. No commands were executed. Put all commands into a single <execute> block.\n", Color.Red);
				return;
			}

			int start = content.IndexOf(ExecuteOpenTag, StringComparison.OrdinalIgnoreCase);
			start += ExecuteOpenTag.Length;

			int end = content.IndexOf(ExecuteCloseTag, start, StringComparison.OrdinalIgnoreCase);
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
			{	ClearTranscript();
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
