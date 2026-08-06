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

		private readonly Ensemble ensemble = new Ensemble();

		private string[] pendingAnswers;  // finished answers waiting for a free moment to be run
		private string lastConversation;  // this turn's message, kept for the rethink
		private bool rethinkSent;         // one rethink per turn, then the bot acts anyway

		// The tail of the batch the streams did not agree on. Reported after the executed commands
		// so the transcript keeps the real order of events.
		private readonly List<string> notExecuted = new List<string>();

		// Sent on top of the same conversation, identical for both streams, and never stored in the
		// transcript: if the second pass agrees, the turn must read as if it answered once.
		private const string Rethink =
			"\n[SYSTEM] Think again before acting. Re-read the results above and look for a mistake in your own"
			+ " reasoning. If you find one, answer with the corrected commands; if not, repeat the same commands.\n";

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
				FlushNotExecuted();
				return;
			}

			if(batch.Count == 0) FlushNotExecuted();

			// Next command (if any) is driven by Tick() — one per tick.
		}

		private void FlushNotExecuted()
		{
			if (notExecuted.Count == 0) return;

			Append($"Not executed: {string.Join("; ", notExecuted)}\n", Color.Cornsilk);
			notExecuted.Clear();
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
			PollStreams();
				// stores data to pendingAnswers

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

			if(ensemble.Busy) return;
			if(pause) return;

			// Process a finished response BEFORE any send. Otherwise an async sensor
			// report (VISION/STATUS) can fire the send below and orphan it; the next
			// response then appends onto it ("Found 2 <execute> blocks").
			if(pendingAnswers != null)
			{	var answers = pendingAnswers;
				pendingAnswers = null;
				ProcessAnswers(answers);
				if(ensemble.Busy) return;      // a rethink went out — this turn is not over
				if(batch.Count != 0) return;   // commands enqueued — execute before talking to LLM
				if(pause) return;              // response was pause/restart — do not send this turn
			}

			if (output.Length != 0) // We have data for LLM
			{
				int used = ContextUsed;
				int total = ensemble.ContextWindow;

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

				lastConversation = "\n" + string.Join("\n", transcript);
				rethinkSent = false;
				ensemble.Send(lastConversation);
				turn++;
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
			int total = ensemble.ContextWindow;
			if (total <= 0) return;
			int used = ContextUsed;
			MyConsole.Add($"[CONTEXT] {used}/{total} chars ({used * 100 / total}%)", Color.LightPink);
		}

		private void PollStreams()
		{
			string[] answers;
			if (!ensemble.Poll(out answers)) return;

			pendingAnswers = answers;
			ContextStatistic();
		}

		private static string MyTrim(string s)
		{	if(s.Length < 2) return s;
			char fc = s[0];
			if ((fc == '`' || fc == '\'' || fc == '\"') && s[s.Length - 1] == fc) return s.Substring(1, s.Length - 2);
			return s;
		}

		// The <execute> block as the mod reads it. Returns null and fills 'error' with the text the
		// model will read next turn — nothing here touches the game or the transcript.
		private static List<string> ParseBatch(string content, out string error)
		{
			error = null;
			content = content.Trim();

			// Exactly one <execute> block is allowed; multiple blocks are rejected (not "keep the last").
			int count = 0, scan = 0;
			while (true)
			{	int at = content.IndexOf(ExecuteOpenTag, scan, StringComparison.OrdinalIgnoreCase);
				if (at < 0) break;
				count++;
				scan = at + ExecuteOpenTag.Length;
			}
			if (count == 0)
			{	error = "[ERROR] No <execute> block found. Wrap your commands in <execute>...</execute>.\n";
				return null;
			}
			if (count > 1)
			{	error = "[ERROR] Found " + count + " <execute> blocks; exactly one is allowed. No commands were executed. Put all commands into a single <execute> block.\n";
				return null;
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
			{	error = "[ERROR] No commands found inside <execute> block.\n";
				return null;
			}

			return cc;
		}

		// Two commands are the same command when they are literally the same. `say` is the one
		// exception: what the bot tells the player is free text, and two streams never phrase it
		// alike — on the measured session that alone accounted for a sixth of the disagreements.
		private static bool SameCommand(string x, string y)
		{
			if (x.Equals(y, StringComparison.OrdinalIgnoreCase)) return true;

			return x.StartsWith("say ", StringComparison.OrdinalIgnoreCase)
			    && y.StartsWith("say ", StringComparison.OrdinalIgnoreCase);
		}

		// Commands are order-dependent (fly, then get), so agreement is a prefix, not a set.
		private static int CommonPrefix(List<string> x, List<string> y)
		{
			int n = 0;
			while (n < x.Count && n < y.Count && SameCommand(x[n], y[n])) n++;
			return n;
		}

		// Both streams answered the same question. What they both said is what the bot does; where
		// their first commands differ, nothing runs and the bot is asked to think again — once.
		private void ProcessAnswers(string[] answers)
		{
			var batches = new List<string>[Ensemble.Streams];
			var errors  = new string[Ensemble.Streams];

			int spoke = -1;              // first stream that answered at all
			int first = -1, second = -1; // the streams whose answer parsed

			for (int i = 0; i < Ensemble.Streams; ++i)
			{
				if (answers[i] == null) continue;
				if (spoke < 0) spoke = i;

				batches[i] = ParseBatch(answers[i], out errors[i]);
				if (batches[i] == null) continue;

				if (first < 0) first = i; else if (second < 0) second = i;
			}

			if (spoke < 0)
			{	// Every stream died on the way. Nothing to read and nothing to run.
				MyConsole.AddMultiline("\n[LLM ERROR] " + ensemble.Error + "\n", Color.Red);
				pause = true;
				return;
			}

			int leader = first >= 0 ? first : spoke; // whose text goes into the transcript
			var cc = batches[leader];
			int run = cc == null ? 0 : cc.Count;     // how many of its commands to execute

			if (second >= 0)
			{
				run = CommonPrefix(batches[first], batches[second]);

				if (run == 0 && !rethinkSent)
				{	rethinkSent = true;
					Log($"consensus: turn {turn}, disagreed on '{batches[first][0]}' vs '{batches[second][0]}' — asking again");
					MyConsole.AddNewLine();
					MyConsole.Add("[RETHINK] streams disagreed on the first command", Color.Yellow);

					// Both answers are dropped, transcript and all: if the second pass agrees, this
					// turn must read as if the bot answered once.
					ensemble.Send(lastConversation + Rethink);
					return;
				}

				// Still disagreeing after the rethink. One command, not a plan: the streams part
				// where the model is unsure, and a five-step plan built on an unsure first step is
				// exactly what this scheme exists to stop. One command brings back a fact from the
				// game, and the next turn decides again with it in hand.
				if (run == 0)
				{	run = 1;
					Log($"consensus: turn {turn}, still disagreed after the rethink, running one command");
				}
			}

			// Every turn leaves its numbers in the log: how much of the batch survived the vote is
			// the whole point of running two streams, and it is only measurable in play.
			Log($"consensus: turn {turn}, ran {run} of {(cc == null ? 0 : cc.Count)}"
				+ (second >= 0 ? $", {batches[second].Count} proposed by the other stream" : ", single stream"));

			// The bot's own words go back into the transcript. Without them it reads a list of
			// results with no record of what it said or meant. Reasoning stays out: the chat
			// template drops thought from previous turns anyway.
			// Console already printed it while streaming; llmContent already logged it.
			Append($"[YOU]:\n{answers[leader].Trim()}\n", Color.Cyan, Destination.LLM);
			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);

			if (cc == null)
			{	Append(errors[leader], Color.Red);
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
				{	Append($"Your last message was:\n---\n{answers[leader].Trim()}\n---\n", Color.Red);
					return;
				}
			}

			// Queue the agreed prefix; the tail is reported as dropped once the prefix has run.
			for (int i = 0; i < cc.Count; ++i)
			{	if (i < run) batch.Enqueue(cc[i]);
				else notExecuted.Add(cc[i]);
			}

			RunNextPending();
		}
	}
}
