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

		private readonly LlmChannel channel = new LlmChannel(0);

		private Answer response;
		private string responseError;

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
		}

		private void AddAssistant(Answer answer)
		{
			callCursor = 0;
			callIdBase = answer.FirstCallId;
			transcript.Add(answer.AssistantJson);
			transcriptChars += answer.AssistantJson.Length;
		}

		private void AnswerRest(int total, string text)
		{
			while (callCursor < total) AnswerCall(text);
		}

		private void AnswerCall(string text)
		{
			var json = new StringBuilder("{\"role\":\"tool\",\"tool_call_id\":");
			Json.Quoted(json, LlmChannel.CallId(callIdBase + callCursor));
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
			PollChannel();

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
				{	var result = commands.Update();
					if (result != null)
						OnCommandFinished(result);
				}
				else
					RunNextPending();

				return;
			}

			if(channel.Busy) return;
			if(pause) return;

			// Must stay ahead of the send below: an async VISION/STATUS report would otherwise
			// fire that send and orphan it, and the next response appends its calls onto it.
			if(response != null || responseError != null)
			{	ProcessAnswer();
				if(batch.Count != 0) return;
				if(pause) return;
			}

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

				int reserve = total / 5;

				if (used + reserve > total)
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

		private void ProcessAnswer()
		{
			var answer = response;
			response = null;

			if (answer == null)
			{	MyConsole.AddMultiline("\n[LLM ERROR] " + responseError + "\n", Color.Red);
				responseError = null;
				pause = true;
				return;
			}

			var cc = answer.Calls;

			string error = answer.Error;
			if (error == null && cc.Count == 0)
				error = "[ERROR] You called no tool.\n";

			Append("[YOU]: /llmContent/\n", Color.Cyan, Destination.Log);
			AddAssistant(answer);

			if (error != null)
			{	Log($"turn {turn} unusable: {error.Trim()}");
				Append(error, Color.Red);
				return;
			}

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
				contextWarnStage = 0;
				return;
			}

			if (!cc[0].Is("pause"))
			{
				string loopMsg;
				bool blocked = loopDetector.IsLoop(cc, out loopMsg);
				if (loopMsg != null)
					Append(loopMsg, blocked ? Color.Red : Color.Yellow);
				if (blocked)
				{	AnswerRest(cc.Count, "Blocked: this batch has been repeated too many times.");
					return;
				}
			}

			for (int i = 0; i < cc.Count; ++i) batch.Enqueue(cc[i]);

			RunNextPending();
		}
	}
}
