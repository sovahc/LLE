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
		private int repeats;

		public bool IsLoop(List<ToolCall> calls, out string message)
		{
			var current = LLM.Join("\n", calls);
			if (current == lastBatch)
				repeats++;
			else
			{	lastBatch = current;
				repeats = 1;
			}

			// An answer with no call at all never gets here, so the batch was notes and nothing else.
			if (calls.Count == 0)
			{	message = "!ERROR: A note is not a command. Follow it with the command you decided on.\n";
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

		private readonly List<string> transcript = new List<string>();
		private int transcriptChars;
		private int turn;

		private int callCursor;
		private int callIdBase;

		private bool hasNews;

		// Without the loader the stream reports no context, and every turn stops before the send.
		private readonly LlmChannel channel = new LlmChannel();

		private Answer answer;
		private string error;
		private bool answered;

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
		private readonly StringBuilder logBuf = new StringBuilder();

		public static bool pause;
		public static int contextWarnStage;

		private Commands commands;

		private Queue<ToolCall> batch = new Queue<ToolCall>();

		private double commandStartTime;

		private readonly LoopDetector loopDetector = new LoopDetector();

		private bool restartPending;

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

			var destination = Destination.Console | Destination.Log;

			Append($"→ {currentCommand.Text}: [{tag}] {result.Message}{took}\n", Color.Cornsilk, destination);
			AnswerCall($"[{tag}] {result.Message}{took}");

			if(result.Status != CommandStatus.Success && batch.Count > 0)
			{	Append($"Remaining {batch.Count} command(s) ignored: {Join("; ", new List<ToolCall>(batch))}\n",
					Color.Cornsilk, destination);

				while(batch.Count > 0)
				{	batch.Dequeue();
					AnswerCall("Not executed: an earlier call in this batch failed.");
				}
			}
		}

		private void AddAssistant(Answer answer)
		{
			callCursor = 0;
			callIdBase = answer.FirstCallId;
			transcript.Add(answer.AssistantJson);
			transcriptChars += answer.AssistantJson.Length;
		}

		private void AnswerRest(int total, string text, bool news = true)
		{
			while (callCursor < total) AnswerCall(text, news);
		}

		private void AnswerCall(string text, bool news = true)
		{
			var json = new StringBuilder("{\"role\":\"tool\",\"tool_call_id\":");
			Json.Quoted(json, LlmChannel.CallId(callIdBase + callCursor));
			json.Append(",\"content\":");
			Json.Quoted(json, text);
			json.Append('}');

			transcript.Add(json.ToString());
			transcriptChars += json.Length;
			callCursor++;
			if (news) hasNews = true;
		}

		// A throwing command has to stop here: an exception escaping the update loop reaches
		// MyProgram.Main and takes the game down with it.
		private CommandResult CommandCrashed(ToolCall call, Exception e)
		{	LLE.Log($"Command '{call.Text}' threw: {e}");
			return $"Internal error: command '{call.Text}' crashed with {e.GetType().Name}: {e.Message}";
		}

		private void RunNextPending()
		{
			if (batch.Count == 0) return;

			commandStartTime = Time.Now;

			CommandResult result;
			try
			{	result = commands.Execute(batch.Peek()); }
			catch(Exception e)
			{	result = CommandCrashed(batch.Peek(), e); }

			if (result == null && !commands.InProgress())
				result = $"Internal error: command '{batch.Peek().Text}' produced no result.";

			if (result != null)
				OnCommandFinished(result);
		}

		public void Append(string text, Color consoleColor, Destination d = Destination.All)
		{	if((d & Destination.Console) != 0) MyConsole.AddMultiline(text, consoleColor);
			if((d & Destination.Log)     != 0) logBuf.Append(text);
			if((d & Destination.LLM)     != 0) { output.Append(text); hasNews = true; }
		}

		public void Tick()
		{
			Poll();

			var ec = commands.GetEngineerCenter();

			Vision.Tick(ec);
			string vr = Vision.VisionReport(ec);
			if(vr != null)
			{	Append("[VISION]:\n", Color.Yellow);
				Append(vr, Color.Yellow);
				pause = false;
			}

			commands.Draft_Tick();

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
				if(commands.InProgress())
				{	CommandResult result;
					try
					{	result = commands.Update(); }
					catch(Exception e)
					{	// The half-run coroutine goes with the exception: left on the stack it walks
						// back into the same crash on the next tick.
						commands.AbortCommand();
						result = CommandCrashed(batch.Peek(), e);
					}
					if (result != null)
						OnCommandFinished(result);
				}
				else
					RunNextPending();
			}

			// A turn is not over until its stream is: the send below would be abandoned by the
			// answer still on its way.
			if(channel.Busy) return;

			// Must stay ahead of the send below: an async VISION/STATUS report would otherwise
			// fire that send and orphan it, and the next response appends its calls onto it.
			if(answered)
				ProcessAnswer();

			if(restartPending && batch.Count == 0) Restart();

			if(pause) return;

			// Slots still open: sending now would leave the model's own tool calls unanswered.
			if (batch.Count != 0) return;

			if (hasNews)
			{
				int used = ContextUsed;
				int total = channel.ContextChars;

				if (total <= 0)
				{	output.Clear();
					logBuf.Clear();
					hasNews = false;
					return;
				}

				if (used > total)
				{
					ClearTranscript();
					commands.SetSystemPromptAndMemory();
					Append("[CONTEXT AUTO-RESET — context was full]\n", Color.Red);
					contextWarnStage = 0;
				}
				else
				{	int pct = used * 100 / total;

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

				Log($"toLLM: {logBuf}");

				var message = UserMessage(output.Length == 0 ? "..." : output.ToString());
				transcript.Add(message);
				transcriptChars += message.Length;
				output.Clear();
				logBuf.Clear();
				hasNews = false;

				channel.Send(Request());
				turn++;
				return;
			}

		}

		private string Request()
		{
			var sb = new StringBuilder("\"messages\":[{\"role\":\"system\",\"content\":");
			Json.Quoted(sb, commands.SystemPrompt);
			sb.Append('}');

			foreach (var message in transcript)
				sb.Append(',').Append(message);

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
			int total = channel.ContextChars;
			if (total <= 0) return;
			int used = ContextUsed;
			MyConsole.Add($"[CONTEXT] {used}/{total} chars ({used * 100 / total}%)", Color.LightPink);
		}

		// Both fields are written on every event: once `answered` is set, exactly one of them
		// is filled, and a fresh event never leaves the previous turn's answer behind.
		private void Poll()
		{
			Answer payload;
			string errorText;

			var polled = channel.Poll(out payload, out errorText);
			if (polled == ChannelEvent.None) return;

			if (polled == ChannelEvent.Error)
			{	Log($"llm died: {errorText}");
				answer = null;
				error = errorText;
			}
			else
			{	answer = payload;
				error = null;
			}

			answered = true;
		}

		// `note` touches nothing, so checking it says nothing about the answer.
		private static List<ToolCall> Acting(List<ToolCall> calls)
		{
			var acting = new List<ToolCall>();
			foreach (var call in calls)
				if (!call.Is("note")) acting.Add(call);
			return acting;
		}

		private void ProcessAnswer()
		{
			answered = false;
			ContextStatistic();

			if (answer == null)
			{	MyConsole.AddMultiline("\n[LLM ERROR] " + error + "\n", Color.Red);
				pause = true;
				return;
			}

			var cc = answer.Calls;

			string answerError = answer.Error;
			if (answerError == null && cc.Count == 0)
				answerError = "[ERROR] You called no tool.\n";

			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
			AddAssistant(answer);

			if (answerError != null)
			{	Log($"turn {turn} unusable: {answerError.Trim()}");
				Append(answerError, Color.Red);
				return;
			}

			bool hasControl = cc.Any(c => c.Is("restart") || c.Is("pause"));
			if (hasControl && !cc.All(Harmless))
			{
				Append("[ERROR] pause and restart can only be batched with note and say.\n",
					Color.Red, Destination.Console | Destination.Log);
				AnswerRest(cc.Count, "Not executed: pause and restart can only be batched"
					+ " with note and say.");
				return;
			}

			restartPending = cc.Any(c => c.Is("restart"));

			if (!hasControl)
			{
				// The note reads differently every turn; left in, no repeat would ever look like one.
				string loopMsg;
				bool blocked = loopDetector.IsLoop(Acting(cc), out loopMsg);
				if (loopMsg != null)
					Append(loopMsg, blocked ? Color.Red : Color.Yellow);
				if (blocked)
				{	AnswerRest(cc.Count, "Blocked: this batch has been repeated too many times.");
					return;
				}
			}

			for (int i = 0; i < cc.Count; ++i)
				if (!cc[i].Is("restart")) batch.Enqueue(cc[i]);

			RunNextPending();
		}

		private static bool Harmless(ToolCall call)
		{	return call.Is("restart") || call.Is("pause") || call.Is("note") || call.Is("say");
		}

		// The transcript answers the calls of the batch, so it can only be dropped once they are done.
		private void Restart()
		{	restartPending = false;
			ClearTranscript();
			commands.SetSystemPromptAndMemory();
			Append("[CONTEXT RESET]\n", Color.LightGreen);
			loopDetector.Reset();
			contextWarnStage = 0;
		}
	}
}
