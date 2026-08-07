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

		private readonly LlmChannel channel = new LlmChannel(0);

		// The answer this turn will act on, once it has arrived whole. 'response' is what the model
		// said; 'responseError' is filled instead when the channel died on the way. Either one means
		// there is something to act on and nothing to send.
		private Answer response;
		private string responseError;

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
			}

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
			transcriptChars += json.Length;
			callCursor++;
			hasNews = true;
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
			PollChannel();
				// stores the finished response, acted on further down

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

			if(channel.Busy) return;
			if(pause) return;

			// Process a finished response BEFORE any send. Otherwise an async sensor
			// report (VISION/STATUS) can fire the send below and orphan it; the next
			// response then appends its own calls onto it.
			if(response != null || responseError != null)
			{	ProcessAnswer();
				if(batch.Count != 0) return;   // commands enqueued — execute before talking to LLM
				if(pause) return;              // response was pause/restart — do not send this turn
			}

			if (hasNews) // We have data for LLM
			{
				int used = ContextUsed;
				int total = channel.ContextWindow;

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
								+ " Save anything you must not forget with the memory tool.\n", Color.Yellow);
						else if(stage == 2)
							Append($"!WARNING: Context is {pct}% full."
								+ " Save your state now with the memory tool"
								+ ", then reset it yourself with restart.\n", Color.Yellow);
						else
							Append($"!ERROR: Context is {pct}% full and will be wiped automatically very soon."
								+ " You must save your state with the memory tool and then call restart."
								+ " Everything not in memory will be lost.\n", Color.Red);
					}
				}

				// Send accumulated results to LLM
				Log($"toLLM: {logBuf}");

				var text = output.Length == 0 ? "..." : output.ToString();
				transcript.Add(UserMessage(text));
				transcriptChars += text.Length;
				output.Clear();
				logBuf.Clear();
				hasNews = false;

				channel.Send(Request());
				turn++;
				return;
			}

		}

		// The conversation as it goes out.
		private string Request()
		{
			var sb = new StringBuilder("\"messages\":[{\"role\":\"system\",\"content\":");
			Json.Quoted(sb, commands.SystemPrompt);
			sb.Append('}');

			foreach (var message in transcript)
				sb.Append(',').Append(message);

			// A frame is offered once and belongs to the turn that takes it. It rides in a message
			// of its own, at the end, where the model is looking now.
			var screenshot = LLE_Loader.TakeScreenshot();
			if (screenshot != null)
			{
				sb.Append(",{\"role\":\"user\",\"content\":[{\"type\":\"image_url\",\"image_url\":{\"url\":");
				Json.Quoted(sb, "data:image/png;base64," + screenshot);
				sb.Append("}}]}");
			}

			sb.Append("],\"tools\":").Append(Tools.Schema());
			return sb.ToString();
		}

		private static string UserMessage(string text)
		{	var sb = new StringBuilder("{\"role\":\"user\",\"content\":");
			Json.Quoted(sb, text);
			return sb.Append('}').ToString();
		}

		private int ContextUsed
		{	get { return commands.SystemPromptChars + transcriptChars + Tools.Schema().Length; }
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

		// The answer is only stored here. What the turn does with it is decided in Tick, after the
		// sensors have had their say — a response acted on the moment it lands would run its batch
		// before the news of this frame reaches the transcript.
		private void PollChannel()
		{
			Answer payload;
			string errorText;

			var polled = channel.Poll(out payload, out errorText);
			if (polled == ChannelEvent.None) return;

			if (polled == ChannelEvent.Error)
			{	Log($"channel died: {errorText}");
				responseError = errorText;
			}
			else response = payload;

			ContextStatistic();
		}

		// One answer, and the batch it asked for. What the model called is what runs.
		private void ProcessAnswer()
		{
			var answer = response;
			response = null;

			if (answer == null)
			{	// The channel died on the way. Nothing to read and nothing to run.
				MyConsole.AddMultiline("\n[LLM ERROR] " + responseError + "\n", Color.Red);
				responseError = null;
				pause = true;
				return;
			}

			var cc = answer.Calls;

			// A call that came back unreadable, and a turn that called nothing at all: neither can be
			// run. What goes on record is what the model said, with the error it earned against it.
			string error = answer.Error;
			if (error == null && cc.Count == 0)
				error = "[ERROR] You called no tool.\n";

			// The bot's turn goes into the transcript as its own message, and the calls travel as
			// calls. Reasoning stays out — the chat template drops thought from previous turns anyway.
			// Console already printed it while streaming; llmContent already logged it.
			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
			AddAssistant(answer.AssistantJson);

			if (error != null)
			{	Log($"turn {turn} unusable: {error.Trim()}");
				Append(error, Color.Red);
				return;
			}

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

			for (int i = 0; i < cc.Count; ++i) batch.Enqueue(cc[i]);

			RunNextPending();
		}
	}
}
