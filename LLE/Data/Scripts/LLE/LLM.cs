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

	// One plan on the table: a stream's own words and the commands parsed out of them. Round one
	// puts one of these in the pool per stream, the choice round adds one more per stream, and the
	// turn is decided over the pool as a whole.
	class Proposal
	{
		public readonly string Answer;
		public readonly List<string> Commands;

		public Proposal(string answer, List<string> commands)
		{	Answer = answer;
			Commands = commands;
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
		private string lastConversation;  // this turn's message, kept for the choice round
		private bool choiceSent;          // one choice round per turn, then the bot acts anyway

		// Every plan this turn has produced, round one first. The choice round adds to it instead
		// of replacing it: a plan the streams converge on wins by the same rule as any other.
		private readonly List<Proposal> pool = new List<Proposal>();

		// The tail of the batch the streams did not agree on. Reported after the executed commands
		// so the transcript keeps the real order of events.
		private readonly List<string> notExecuted = new List<string>();

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
				if(ensemble.Busy) return;      // a tie-break went out — this turn is not over
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
				choiceSent = false;
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

		// The largest group of proposals starting with the same command, and the commands the whole
		// group agrees on. A group of one is not agreement: a plan nobody else proposed wins
		// nothing here. On a tie the earlier group keeps it — round one is older than the choice.
		private static bool Agreed(List<Proposal> plans, out Proposal winner, out int run)
		{
			winner = null;
			run = 0;

			int best = 1;

			for (int i = 0; i < plans.Count; ++i)
			{
				int count = 1;
				int prefix = plans[i].Commands.Count;

				for (int j = i + 1; j < plans.Count; ++j)
				{
					if (!SameCommand(plans[i].Commands[0], plans[j].Commands[0])) continue;

					count++;
					int p = CommonPrefix(plans[i].Commands, plans[j].Commands);
					if (p < prefix) prefix = p;
				}

				if (count > best)
				{	best = count;
					winner = plans[i];
					run = prefix;
				}
			}

			return winner != null;
		}

		// Ties go to the earlier plan; there is nothing to tell two plans of the same length apart.
		private static Proposal Shortest(List<Proposal> plans)
		{
			var best = plans[0];
			foreach (var p in plans)
				if (p.Commands.Count < best.Commands.Count) best = p;
			return best;
		}

		// The choice round. Every stream gets the same text and none is told which plan was its
		// own: a stream asked to defend its answer defends it, and what is wanted here is a choice.
		//
		// It asks for a choice, not for more thought. Measured head to head on the hard turn of a
		// logged session (6 samples each, two plans): this wording reasons 1601 chars in 8.5s and
		// all six samples pick the same plan — and it is the right one. The wording it replaced
		// ("think again, look for a mistake in your own reasoning") reasoned 4705 chars in 16.7s
		// and five of six answered with something that was neither plan.
		//
		// "without its label" is not decoration: without it a third of the answers came back as the
		// literal text "PLAN A:" and its lines, with no <execute> block at all.
		private static string Choice(List<Proposal> plans)
		{
			string count = plans.Count == 3 ? "three" : "two";

			var sb = new StringBuilder();

			sb.Append("\n[SYSTEM] ").Append(plans.Count == 3 ? "Three" : "Two")
				.Append(" plans were proposed for this turn and they start differently.")
				.Append(" Pick the one that is right here.")
				.Append(" Answer with a single <execute> block holding that plan's commands, unchanged, without its label,")
				.Append(" and nothing else. Do not think it over and do not write a plan of your own:")
				.Append(" this is a choice between ").Append(count).Append(".")
				.Append(" If none of them is right, answer with the one command that fixes that.\n");

			for (int i = 0; i < plans.Count; ++i)
				sb.Append("PLAN ").Append((char)('A' + i)).Append(":\n")
					.Append(string.Join("\n", plans[i].Commands)).Append("\n");

			return sb.ToString();
		}

		// The streams answered the same question. Two of them starting with the same command is
		// agreement and their shared prefix runs. All of them parting company sends every plan back
		// to every stream — that round is the choice, and its answers go into the same pool, so the
		// plan the streams converge on wins by the same rule as before.
		private void ProcessAnswers(string[] answers)
		{
			if (!choiceSent) pool.Clear(); // a new turn — the choice round adds to what round one left

			var errors = new string[Ensemble.Streams];
			int spoke = -1;                // first stream that answered at all

			for (int i = 0; i < Ensemble.Streams; ++i)
			{
				if (answers[i] == null) continue;
				if (spoke < 0) spoke = i;

				var parsed = ParseBatch(answers[i], out errors[i]);
				if (parsed != null) pool.Add(new Proposal(answers[i], parsed));
			}

			if (spoke < 0)
			{	// Every stream died on the way. Nothing to read and nothing to run.
				MyConsole.AddMultiline("\n[LLM ERROR] " + ensemble.Error + "\n", Color.Red);
				pause = true;
				return;
			}

			if (pool.Count == 0)
			{	// Nobody wrote a batch the mod can read — not in this round and not in the one
				// before it. The first stream's words go back with the error it earned.
				Append($"[YOU]:\n{answers[spoke].Trim()}\n", Color.Cyan, Destination.LLM);
				Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
				Append(errors[spoke], Color.Red);
				return;
			}

			Proposal winner;
			int run;

			if (!Agreed(pool, out winner, out run))
			{
				if (!choiceSent && pool.Count > 1)
				{	choiceSent = true;

					Log($"consensus: turn {turn}, no two streams agreed: "
						+ string.Join(" | ", pool.Select(p => p.Commands[0])) + " — choosing");
					MyConsole.AddNewLine();
					MyConsole.Add("[CHOICE] streams disagreed on the first command", Color.Yellow);

					// The round-one answers are dropped, transcript and all: if the streams converge
					// here, this turn must read as if the bot answered once.
					ensemble.Send(lastConversation + Choice(pool));
					return;
				}

				// Six plans and no two of them start alike — the streams did not converge and there
				// is no third round. The shortest plan is the one that commits least before the
				// game answers back, and the next turn decides again with its result in hand.
				winner = Shortest(pool);
				run = winner.Commands.Count;
				Log($"consensus: turn {turn}, no agreement after the choice, shortest of {pool.Count} wins");
			}

			var cc = winner.Commands;

			// Every turn leaves its numbers in the log: how much of the batch survived the vote is
			// the whole point of running three streams, and it is only measurable in play.
			Log($"consensus: turn {turn}, ran {run} of {cc.Count}, pool of {pool.Count}");

			// The bot's own words go back into the transcript. Without them it reads a list of
			// results with no record of what it said or meant. Reasoning stays out: the chat
			// template drops thought from previous turns anyway.
			// Console already printed it while streaming; llmContent already logged it.
			Append($"[YOU]:\n{winner.Answer.Trim()}\n", Color.Cyan, Destination.LLM);
			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);

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
				{	Append($"Your last message was:\n---\n{winner.Answer.Trim()}\n---\n", Color.Red);
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
