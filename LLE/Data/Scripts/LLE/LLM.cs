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
		public bool IsLoop(List<ToolCall> calls, out string message)
		{
			var current = LLM.Join("\n", calls);
			if (current == lastBatch)
				repeats++;
			else
			{	lastBatch = current;
				repeats = 1;
			}

			if (calls.Count == 0)
			{	message = "!ERROR: Your last message called no tool. Every turn is one to three tool calls.\n";
				return repeats >= 4;
			}

			if (repeats == 1)
			{	message = null;
				return false;
			}
			if (repeats == 2)
			{	message = "!Warning: This command batch is identical to the previous one."
					+ " If the task is complete, call pause.\n";
				return false;
			}
			if (repeats == 3)
			{	message = "!WARNING: Identical batch repeated again. Call pause to stop.\n";
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

	// One plan on the table: a stream's own words and the calls it issued alongside them. Round one
	// puts one of these in the pool per stream, the choice round adds one more per stream, and the
	// turn is decided over the pool as a whole.
	class Proposal
	{
		public readonly string Answer;
		public readonly List<ToolCall> Calls;

		public Proposal(string answer, List<ToolCall> calls)
		{	Answer = answer;
			Calls = calls;
		}
	}

	class LLM
	{
		private void Log(string s) => LLE.Log(s);

		// The conversation lives here, not in the loader: the loader is transport and must not
		// hold state that belongs to a turn. Each entry is one message, already written as the JSON
		// the request carries — the bot's own turns are kept as they came off the wire and are never
		// rewritten.
		private readonly List<string> transcript = new List<string>();
		private int transcriptChars;
		private int turn;

		// How many calls of the batch being executed have answered so far. The position is what
		// names the call a result belongs to.
		private int callCursor;

		// Something happened that the model has not seen. Not the same as "output is not empty":
		// a turn whose whole news is command results has nothing in output at all, and skipping
		// the send would leave the bot waiting on a conversation that never continues.
		private bool hasNews;

		private readonly Ensemble ensemble = new Ensemble();

		private bool choiceSent;          // one choice round per turn, then the bot acts anyway

		private bool inFlight;            // a round is out with the streams
		private bool decided;             // the pool holds everything this turn will act on
		private bool anyoneSpoke;         // at least one stream answered rather than died
		private string unparsedAnswer;    // first answer with no readable batch, kept for the report
		private string unparsedError;

		// Every plan this turn has produced, round one first. The choice round adds to it instead
		// of replacing it: a plan the streams converge on wins by the same rule as any other.
		private readonly List<Proposal> pool = new List<Proposal>();

		// The tail of the batch the streams did not agree on. Reported after the executed commands
		// so the transcript keeps the real order of events.
		private readonly List<ToolCall> notExecuted = new List<ToolCall>();

		// Calls print as themselves everywhere they are shown or compared.
		public static string Join(string separator, List<ToolCall> calls)
		{	var sb = new StringBuilder();
			for (int i = 0; i < calls.Count; ++i)
			{	if (i != 0) sb.Append(separator);
				sb.Append(calls[i].Text);
			}
			return sb.ToString();
		}

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

		private Queue<ToolCall> batch = new Queue<ToolCall>();

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

			// The result goes back as the answer to its own call, so it does not repeat which call
			// that was. The console still shows the pair — nobody reading it has the request in front
			// of them.
			Append($"→ {currentCommand.Text}: [{tag}] {result.Message}{took}\n", Color.Cornsilk,
				Destination.Console | Destination.Log);
			AnswerCall($"[{tag}] {result.Message}{took}");

			if(result.Status != CommandStatus.Success && batch.Count > 0)
			{	Append($"Remaining {batch.Count} command(s) ignored: {Join("; ", new List<ToolCall>(batch))}\n",
					Color.Cornsilk, Destination.Console | Destination.Log);

				while(batch.Count > 0)
				{	batch.Dequeue();
					AnswerCall("Not executed: an earlier call in this batch failed.");
				}

				FlushNotExecuted();
				return;
			}

			if(batch.Count == 0) FlushNotExecuted();

			// Next command (if any) is driven by Tick() — one per tick.
		}

		// The bot's turn, exactly as the channel wrote it down. Nothing here looks inside it: the
		// calls it holds are the model's own bytes, and the results that follow answer them by
		// position.
		private void AddAssistant(string assistantJson)
		{
			callCursor = 0;
			transcript.Add(assistantJson);
			transcriptChars += assistantJson.Length;
		}

		// Whatever is left of the batch answers with the same reason. A call the bot made and the
		// mod then dropped still has to come back answered.
		private void AnswerRest(int total, string text)
		{
			while (callCursor < total) AnswerCall(text);
		}

		// Every call the bot made has to come back answered, in the order it was made: that is what
		// ties a result to its call, and a call left unanswered breaks the turn it belongs to.
		private void AnswerCall(string text)
		{
			var json = new StringBuilder("{\"role\":\"tool\",\"tool_call_id\":");
			Json.Quoted(json, LlmChannel.CallId(callCursor));
			json.Append(",\"content\":");
			Json.Quoted(json, text);
			json.Append('}');

			transcript.Add(json.ToString());
			transcriptChars += text.Length;
			callCursor++;
			hasNews = true;
		}

		private void FlushNotExecuted()
		{
			if (notExecuted.Count == 0) return;

			Append($"Not executed: {Join("; ", notExecuted)}\n", Color.Cornsilk,
				Destination.Console | Destination.Log);

			// Why, not just what: with no reason given the model spends its next turn reasoning about
			// what "not executed" could mean instead of reading the results it did get.
			foreach (var call in notExecuted)
				AnswerCall("Not executed — the environment dropped the tail of the batch, which is"
					+ " normal. Continue from the results you did get; do not repeat what succeeded.");

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
				result = $"Internal error: command '{batch.Peek().Text}' produced no result.";

			if (result != null)
			{
				// Synchronous command — continue immediately
				OnCommandFinished(result);
			}
		}

		public void Append(string text, Color consoleColor, Destination d = Destination.All)
		{	if((d & Destination.Console) != 0) MyConsole.AddMultiline(text, consoleColor);
			if((d & Destination.Log)     != 0) logBuf.Append(text);
			if((d & Destination.LLM)     != 0) { output.Append(text); hasNews = true; }
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

			if(ensemble.Busy) return;
			if(pause) return;

			// Process a finished response BEFORE any send. Otherwise an async sensor
			// report (VISION/STATUS) can fire the send below and orphan it; the next
			// response then appends its own calls onto it.
			if(decided)
			{	decided = false;
				ProcessAnswers();
				if(ensemble.Busy) return;      // a tie-break went out — this turn is not over
				if(batch.Count != 0) return;   // commands enqueued — execute before talking to LLM
				if(pause) return;              // response was pause/restart — do not send this turn
			}

			if (hasNews) // We have data for LLM
			{
				int used = ContextUsed;
				int total = ensemble.ContextWindow;

				if (total <= 0)
				{	// No such channel in the loader config — there is nobody to talk to.
					output.Clear();
					logBuf.Clear();
					hasNews = false;
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

				// What the environment has to say goes in as one user message; the command results
				// are already in, each answering its own call.
				//
				// It goes in even when there is nothing to say. A conversation that ends on a tool
				// result gets no generation prompt from Gemma's chat template — it breaks off inside
				// the model's own turn, and the model then opens the turn by hand and writes the
				// template's own tokens out as text. Measured over 16 turns: 5 of 9 usable when the
				// results end the conversation, 15 of 16 when a user message closes them.
				var text = output.Length == 0 ? "[SYSTEM] No new events." : output.ToString();
				transcript.Add(UserMessage(text));
				transcriptChars += text.Length;
				output.Clear();
				logBuf.Clear();
				hasNews = false;

				choiceSent = false;
				pool.Clear();
				unparsedAnswer = null;
				anyoneSpoke = false;
				inFlight = true;

				ensemble.Send(Request(null));
				turn++;
				return;
			}

		}

		// The conversation as it goes out. The choice round asks its question as one more user
		// message rather than by editing the last one: the turn it is asking about has to read the
		// same way it did the first time.
		private string Request(string extraUser)
		{
			var sb = new StringBuilder("\"messages\":[{\"role\":\"system\",\"content\":");
			Json.Quoted(sb, commands.SystemPrompt);
			sb.Append('}');

			foreach (var message in transcript)
				sb.Append(',').Append(message);

			if (extraUser != null)
				sb.Append(',').Append(UserMessage(extraUser));

			// A frame is offered once and belongs to the turn that takes it. It rides in a message
			// of its own, at the end, where the model is looking now.
			var screenshot = LLE_Loader.TakeScreenshot();
			if (screenshot != null)
			{
				sb.Append(",{\"role\":\"user\",\"content\":[{\"type\":\"image_url\",\"image_url\":{\"url\":");
				Json.Quoted(sb, "data:image/png;base64," + screenshot);
				sb.Append("}}]}");
			}

			sb.Append("],\"tools\":").Append(Tools.Json());
			return sb.ToString();
		}

		private static string UserMessage(string text)
		{	var sb = new StringBuilder("{\"role\":\"user\",\"content\":");
			Json.Quoted(sb, text);
			return sb.Append('}').ToString();
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

		// Answers are taken as they land, not as a set: the pool is what the turn is decided on, and
		// once it decides, the streams still generating are told to stop.
		private void PollStreams()
		{
			Answer answer;
			int stream;

			while ((stream = ensemble.Poll(out answer)) >= 0)
			{
				if (answer == null)
				{	Log($"stream {stream} died: {ensemble.Error}");
					continue;
				}

				anyoneSpoke = true;

				// A stream that called nothing has no plan, and one whose call came back unreadable
				// has half of one. Neither reaches the pool; the turn goes on without it. Left
				// unsaid, an answer that never reached the pool shows up nowhere but the pool size,
				// and every count drawn from the log is short by an unknown amount.
				string error = answer.Error;
				if (error == null && answer.Calls.Count == 0)
					error = "[ERROR] You called no tool. Every turn is one to three tool calls.\n";

				if (error != null)
				{	Log($"stream {stream} unusable: {error.Trim()}");

					if (unparsedAnswer == null)
					{	unparsedAnswer = answer.AssistantJson;
						unparsedError = error;
					}
					continue;
				}

				pool.Add(new Proposal(answer.AssistantJson, answer.Calls));
			}

			if (!inFlight || decided) return;

			// Two streams starting with the same command settle the turn: the third can no longer
			// change which plan runs, only shorten it, and waiting on it costs the whole turn. In
			// the choice round every vote still counts, so that one is waited out in full.
			Proposal winner;
			int run;

			if (!choiceSent && Agreed(pool, out winner, out run))
			{	if (ensemble.Busy) Log($"consensus: turn {turn}, decided on {pool.Count}, cancelling the rest");
				ensemble.CancelOutstanding();
			}
			else if (ensemble.Busy) return;

			inFlight = false;
			decided = true;
			ContextStatistic();
		}

		// Two calls are the same call when name and arguments match. `say` is the one exception:
		// what the bot tells the player is free text, and two streams never phrase it alike — on
		// the measured session that alone accounted for a sixth of the disagreements.
		private static bool SameCommand(ToolCall x, ToolCall y)
		{
			if (x.Is("say") && y.Is("say")) return true;

			return x.Text.Equals(y.Text, StringComparison.OrdinalIgnoreCase);
		}

		// Commands are order-dependent (fly, then get), so agreement is a prefix, not a set.
		private static int CommonPrefix(List<ToolCall> x, List<ToolCall> y)
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
				int prefix = plans[i].Calls.Count;

				for (int j = i + 1; j < plans.Count; ++j)
				{
					if (!SameCommand(plans[i].Calls[0], plans[j].Calls[0])) continue;

					count++;
					int p = CommonPrefix(plans[i].Calls, plans[j].Calls);
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
				if (p.Calls.Count < best.Calls.Count) best = p;
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
		// literal text "PLAN A:" and its lines, with no call at all.
		private static string Choice(List<Proposal> plans)
		{
			string count = plans.Count == 3 ? "three" : "two";

			var sb = new StringBuilder();

			sb.Append("\n[SYSTEM] ").Append(plans.Count == 3 ? "Three" : "Two")
				.Append(" plans were proposed for this turn and they start differently.")
				.Append(" Pick the one that is right here.")
				.Append(" Issue that plan's calls, unchanged and in order, without its label,")
				.Append(" and nothing else. Do not think it over and do not write a plan of your own:")
				.Append(" this is a choice between ").Append(count).Append(".")
				.Append(" If none of them is right, answer with the one call that fixes that.\n");

			for (int i = 0; i < plans.Count; ++i)
				sb.Append("PLAN ").Append((char)('A' + i)).Append(":\n")
					.Append(Join("\n", plans[i].Calls)).Append("\n");

			return sb.ToString();
		}

		// The streams answered the same question. Two of them starting with the same command is
		// agreement and their shared prefix runs. All of them parting company sends every plan back
		// to every stream — that round is the choice, and its answers go into the same pool, so the
		// plan the streams converge on wins by the same rule as before.
		private void ProcessAnswers()
		{
			if (!anyoneSpoke)
			{	// Every stream died on the way. Nothing to read and nothing to run.
				MyConsole.AddMultiline("\n[LLM ERROR] " + ensemble.Error + "\n", Color.Red);
				pause = true;
				return;
			}

			if (pool.Count == 0)
			{	// Nobody made a call the mod can use — not in this round and not in the one before
				// it. The stream's words go back with the error they earned.
				Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
				AddAssistant(unparsedAnswer);
				Append(unparsedError, Color.Red);
				return;
			}

			Proposal winner;
			int run;

			if (!Agreed(pool, out winner, out run))
			{
				if (!choiceSent && pool.Count > 1)
				{	choiceSent = true;

					Log($"consensus: turn {turn}, no two streams agreed: "
						+ string.Join(" | ", pool.Select(p => p.Calls[0].Text)) + " — choosing");
					MyConsole.AddNewLine();
					MyConsole.Add("[CHOICE] streams disagreed on the first command", Color.Yellow);

					// The round-one answers are dropped, transcript and all: if the streams converge
					// here, this turn must read as if the bot answered once.
					inFlight = true;
					ensemble.Send(Request(Choice(pool)));
					return;
				}

				// Six plans and no two of them start alike — the streams did not converge and there
				// is no third round. The shortest plan is the one that commits least before the
				// game answers back, and the next turn decides again with its result in hand.
				winner = Shortest(pool);
				run = winner.Calls.Count;
				Log(pool.Count == 1
					? $"consensus: turn {turn}, one proposal, nothing to vote on"
					: $"consensus: turn {turn}, no agreement after the choice, shortest of {pool.Count} wins");
			}

			var cc = winner.Calls;

			// Every turn leaves its numbers in the log: how much of the batch survived the vote is
			// the whole point of running three streams, and it is only measurable in play.
			Log($"consensus: turn {turn}, ran {run} of {cc.Count}, pool of {pool.Count}");

			// The bot's turn goes into the transcript as its own message, and the calls travel as
			// calls. Reasoning stays out — the chat template drops thought from previous turns anyway.
			// Console already printed it while streaming; llmContent already logged it.
			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
			AddAssistant(winner.Answer);

			// Control commands (pause, restart) must be issued alone
			bool hasControl = cc.Any(c => c.Is("restart") || c.Is("pause"));
			if (hasControl && cc.Count > 1)
			{
				Append("[ERROR] pause and restart must be called alone, not mixed with other calls.\n",
					Color.Red, Destination.Console | Destination.Log);
				AnswerRest(cc.Count, "Not executed: pause and restart must be called alone,"
					+ " never in a batch with other calls.");
				return;
			}

			if (cc[0].Is("restart"))
			{	ClearTranscript();
				commands.SetSystemPromptAndMemory();
				Append("[CONTEXT RESET]\n", Color.LightGreen);
				loopDetector.Reset();
				return;
			}

			// pause is exempt from loop detection
			if (!cc[0].Is("pause"))
			{
				string loopMsg;
				bool blocked = loopDetector.IsLoop(cc, out loopMsg);
				if (loopMsg != null)
					Append(loopMsg, blocked ? Color.Red : Color.Yellow);
				if (blocked)
				{	// The batch itself is right above this in the conversation — repeating it here
					// would only be one more example of a call written out as text.
					AnswerRest(cc.Count, "Blocked: this batch has been repeated too many times.");
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
