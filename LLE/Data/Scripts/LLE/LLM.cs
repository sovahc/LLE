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

		private readonly LlmChannel[] channels = BuildChannels();

		private readonly Answer[] answers;
		private readonly string[] errors;
		private bool answered;

		// One stream per configured channel. An absent channel reports no context, and channel zero
		// always exists: without the loader every probe answers zero.
		private static LlmChannel[] BuildChannels()
		{
			var list = new List<LlmChannel>();

			for (int i = 0; i < 8; ++i)
			{	var channel = new LlmChannel(i);
				if (i != 0 && !channel.Present) break;
				list.Add(channel);
			}

			return list.ToArray();
		}

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
			answers = new Answer[channels.Length];
			errors = new string[channels.Length];
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
			PollChannels();

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
			}

			// Every stream is waited out: the answers can only be compared once they all exist.
			foreach(var channel in channels)
				if(channel.Busy) return;

			// Must stay ahead of the send below: an async VISION/STATUS report would otherwise
			// fire that send and orphan it, and the next response appends its calls onto it.
			if(answered)
				ProcessAnswers();

			if(restartPending && batch.Count == 0) Restart();

			if(pause) return;

			// Slots still open: sending now would leave the model's own tool calls unanswered.
			if (batch.Count != 0) return;

			if (hasNews)
			{
				int used = ContextUsed;
				int total = channels[0].ContextChars;

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

				var request = Request();
				foreach(var channel in channels) channel.Send(request);
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
			int total = channels[0].ContextChars;
			if (total <= 0) return;
			int used = ContextUsed;
			MyConsole.Add($"[CONTEXT] {used}/{total} chars ({used * 100 / total}%)", Color.LightPink);
		}

		private void PollChannels()
		{
			for (int i = 0; i < channels.Length; ++i)
			{
				Answer payload;
				string errorText;

				var polled = channels[i].Poll(out payload, out errorText);
				if (polled == ChannelEvent.None) continue;

				if (polled == ChannelEvent.Error)
				{	Log($"channel {i} died: {errorText}");
					errors[i] = errorText;
				}
				else answers[i] = payload;

				answered = true;
			}
		}

		// Every stream answered the same question, so the turn goes to the one whose first command
		// would actually run. The losers are dropped whole: the transcript keeps one voice, and the
		// model is never told it was one of several.
		private void ProcessAnswers()
		{
			answered = false;
			ContextStatistic();

			var scores = new int[answers.Length];
			var refusals = new string[answers.Length];
			int winner = -1;

			for (int i = 0; i < answers.Length; ++i)
			{
				if (answers[i] == null) continue;

				scores[i] = Score(answers[i], out refusals[i]);
				if (winner < 0 || Beats(scores, i, winner)) winner = i;
			}

			if (channels.Length > 1 && winner >= 0) ReportChoice(scores, refusals, winner);

			var answer = winner < 0 ? null : answers[winner];
			var error = winner >= 0 ? null : FirstError();

			for (int i = 0; i < answers.Length; ++i)
			{	answers[i] = null;
				errors[i] = null;
			}

			ProcessAnswer(answer, error);
		}

		// Only the head of a batch is checked, so a longer batch wins on the strength of its head
		// alone. That is the point: one turn spent on four calls beats four turns spent on one.
		private bool Beats(int[] scores, int challenger, int best)
		{
			if (scores[challenger] != scores[best]) return scores[challenger] > scores[best];

			var calls = answers[challenger].Calls.Count - answers[best].Calls.Count;
			if (calls != 0) return calls > 0;

			// Two answers of the same shape: the one that spelled out more of what it is doing.
			return Join("; ", answers[challenger].Calls).Length > Join("; ", answers[best].Calls).Length;
		}

		private const int ScoreRuns = 2;
		private const int ScoreRefused = 1;
		private const int ScoreUnusable = 0;

		// `note` touches nothing, so checking it says nothing about the answer.
		private static List<ToolCall> Acting(List<ToolCall> calls)
		{
			var acting = new List<ToolCall>();
			foreach (var call in calls)
				if (!call.Is("note")) acting.Add(call);
			return acting;
		}

		// Only the first acting call is checked — it decides whether the turn does anything at all.
		// An answer that called no tool still has to lose to any answer that did, and still has to
		// remain the answer when no stream did better.
		private int Score(Answer answer, out string refusal)
		{
			refusal = null;

			if (answer.Error != null || answer.Calls.Count == 0) return ScoreUnusable;

			var acting = Acting(answer.Calls);
			if (acting.Count == 0) return ScoreRuns;

			// restart is answered by the transcript, not by a command, so there is nothing to check.
			if (acting[0].Is("restart")) return ScoreRuns;

			refusal = commands.Validate(acting[0]);
			return refusal == null ? ScoreRuns : ScoreRefused;
		}

		private static string ScoreName(int score)
		{	return score == ScoreRuns ? "ok" : score == ScoreRefused ? "refused" : "no call";
		}

		private void ReportChoice(int[] scores, string[] refusals, int winner)
		{
			for (int i = 0; i < scores.Length; ++i)
			{
				if (answers[i] == null) continue;

				var line = $"[PICK] {i}{(i == winner ? "*" : " ")} {ScoreName(scores[i])}"
					+ (refusals[i] == null ? "" : ": " + refusals[i].Trim())
					+ $" | {Join("; ", answers[i].Calls)}\n";

				Append(line, i == winner ? Color.MediumPurple : Color.Gray, Destination.Console);
				Log(line);
			}
		}

		private string FirstError()
		{
			foreach (var error in errors)
				if (error != null) return error;
			return "no answer";
		}

		private void ProcessAnswer(Answer answer, string channelError)
		{
			if (answer == null)
			{	MyConsole.AddMultiline("\n[LLM ERROR] " + channelError + "\n", Color.Red);
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
