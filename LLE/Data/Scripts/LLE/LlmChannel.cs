using System.Collections.Generic;
using System.Text;

using VRageMath;

namespace LLE
{
	enum ChannelEvent { None, Response, Error }

	class Answer
	{
		public readonly List<ToolCall> Calls;
		public readonly string AssistantJson;

		public readonly int FirstCallId;

		public readonly string Error;

		public Answer(List<ToolCall> calls, string assistantJson, int firstCallId, string error)
		{	Calls = calls;
			AssistantJson = assistantJson;
			FirstCallId = firstCallId;
			Error = error;
		}
	}

	// A stream that has repeated the same block three times over is not going to stop on its own.
	// The period is unknown, so it is scanned: a fixed window would only catch loops whose length
	// divides it. Short periods need more repetitions — "very very very" is speech, not a loop.
	class LoopGuard
	{
		private const int MaxPeriod = 40;
		private const int Window = MaxPeriod * 3;
		private const int MaxWord = 16;

		private readonly List<string> words = new List<string>();
		private readonly StringBuilder word = new StringBuilder();

		public string Tail => string.Join(" ", words);

		public void Clear()
		{	words.Clear();
			word.Clear();
		}

		public bool Feed(string text)
		{
			for (int i = 0; i < text.Length; ++i)
			{
				bool space = char.IsWhiteSpace(text[i]);
				if (!space) word.Append(text[i]);

				// A stream without a single space — a run of "<br>" is the one seen — would grow
				// one endless word and never be looked at.
				if (word.Length == 0 || !space && word.Length < MaxWord) continue;

				words.Add(word.ToString());
				word.Clear();
				if (words.Count > Window) words.RemoveRange(0, words.Count - Window);
			}

			for (int period = 1; period <= MaxPeriod; ++period)
			{
				int repeats = period >= 5 ? 3 : 15 / period;
				if (words.Count >= period * repeats && Repeated(period, repeats)) return true;
			}

			return false;
		}

		private bool Repeated(int period, int repeats)
		{
			int last = words.Count - 1;

			for (int k = 0; k < period; ++k)
			{
				var w = words[last - k];
				for (int r = 1; r < repeats; ++r)
					if (w != words[last - k - r * period]) return false;
			}

			return true;
		}
	}

	class LlmChannel
	{
		public readonly int Id;

		private readonly StringBuilder reasoning = new StringBuilder();
		private readonly StringBuilder content = new StringBuilder();

		private readonly List<StringBuilder> callNames = new List<StringBuilder>();
		private readonly List<StringBuilder> callArguments = new List<StringBuilder>();

		private bool busy;

		private readonly LoopGuard guard = new LoopGuard();
		private int cuts;

		public LlmChannel(int id)
		{	Id = id;
		}

		public int ContextChars { get { return LLE_Loader.GetContextChars(Id); } }
		public bool Present { get { return ContextChars > 0; } }
		public bool Busy { get { return busy; } }

		public void Send(string requestJson)
		{	LLE_Loader.SendMessageToLLM(Id, requestJson);
			busy = true;
		}

		public void Cancel()
		{
			if (!busy) return;

			LLE_Loader.CancelLLM(Id);
			Reset();
			busy = false;
		}

		private void Reset()
		{
			reasoning.Clear();
			content.Clear();
			callNames.Clear();
			callArguments.Clear();
			guard.Clear();
		}

		public ChannelEvent Poll(out Answer answer, out string errorText)
		{
			answer = null;
			errorText = null;

			for (int i = 0; i < 10; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(Id, out m)) return ChannelEvent.None;

				switch (m.Type)
				{
					case MessageType.Chunk:
						if (!ReadChunk(m.Payload)) break;

						LLE.Log($"llmLoop[{Id}]: {guard.Tail}");
						MyConsole.AddMultiline("\n[LOOP] The answer repeated itself and was cut off.\n", Color.Red);
						Cancel();

						// A model that keeps falling into it will not be talked out of it, and every
						// retry costs a whole generation.
						if (++cuts >= 3)
						{	cuts = 0;
							errorText = "the answer looped three times running";
							return ChannelEvent.Error;
						}

						answer = Looped();
						return ChannelEvent.Response;

					case MessageType.Stop:
						MyConsole.AddMultiline("\n", Color.White);
						busy = false;
						cuts = 0;
						answer = Finish();
						Reset();
						return ChannelEvent.Response;

					case MessageType.Error:
						busy = false;
						errorText = m.Payload;
						Reset();
						return ChannelEvent.Error;
				}
			}

			return ChannelEvent.None;
		}

		// True when the guard has seen enough to call the stream a loop.
		private bool ReadChunk(string data)
		{
			string error;
			var root = Json.Parse(data, out error);
			if (root == null)
			{	LLE.Log($"llmChunk[{Id}] unreadable: {error}: {data}");
				return false;
			}

			var choices = root.Field("choices");
			if (choices == null || !choices.Is(JsonKind.Array) || choices.Array.Count == 0) return false;

			var delta = choices.Array[0].Field("delta");
			if (delta == null) return false;

			bool looping = false;

			var think = delta.Field("reasoning_content") ?? delta.Field("reasoning");
			if (think != null && think.Is(JsonKind.String) && think.String.Length != 0)
			{	MyConsole.AddMultiline(think.String, Color.LightGray);
				reasoning.Append(think.String);
				looping |= guard.Feed(think.String);
			}

			var text = delta.Field("content");
			if (text != null && text.Is(JsonKind.String) && text.String.Length != 0)
			{	MyConsole.AddMultiline(text.String, Color.Cyan);
				content.Append(text.String);
				looping |= guard.Feed(text.String);
			}

			var calls = delta.Field("tool_calls");
			if (calls == null || !calls.Is(JsonKind.Array)) return looping;

			foreach (var call in calls.Array)
			{
				var index = call.Field("index");
				int at = index != null && index.Is(JsonKind.Number) ? (int)index.Number : 0;

				if (at < 0 || at >= 64) continue;

				while (callNames.Count <= at)
				{	callNames.Add(new StringBuilder());
					callArguments.Add(new StringBuilder());
				}

				var function = call.Field("function");
				if (function == null) continue;

				var name = function.Field("name");
				if (name != null && name.Is(JsonKind.String)) callNames[at].Append(name.String);

				var arguments = function.Field("arguments");
				if (arguments != null && arguments.Is(JsonKind.String))
				{	callArguments[at].Append(Unquote(arguments.Raw));
					looping |= guard.Feed(arguments.String);
				}
			}

			return looping;
		}

		private const string LoopError =
			"[ERROR] Your answer repeated itself and was cut off. Answer again, shorter, and call a tool.\n";

		// Variant B: the turn is spent, but the looping text stays out of the transcript — left in,
		// it is a worked example for the next turn to imitate.
		private Answer Looped()
		{
			var json = new StringBuilder("{\"role\":\"assistant\",\"content\":");
			Json.Quoted(json, "(cut off: this answer had started repeating itself)");
			json.Append('}');

			return new Answer(new List<ToolCall>(), json.ToString(), 0, LoopError);
		}

		private static string Unquote(string literal)
		{
			if (literal == null || literal.Length < 2) return "";
			return literal.Substring(1, literal.Length - 2);
		}

		private Answer Finish()
		{
			if (reasoning.Length != 0) LLE.Log($"llmReasoning[{Id}]:\n{reasoning}");
			if (content.Length != 0)   LLE.Log($"llmContent[{Id}]:\n{content}");

			var calls = new List<ToolCall>();
			var escaped = new List<string>();
			string firstError = null;

			for (int i = 0; i < callNames.Count; ++i)
			{
				var name = callNames[i].ToString();
				if (name.Length == 0) continue;

				var arguments = callArguments[i].ToString();
				if (arguments.Length == 0) arguments = "{}";

				string error;
				var text = Json.Unescape(arguments, out error);
				var call = text == null ? null : ToolCall.Parse(name, text, out error);

				if (call == null)
				{	LLE.Log($"llmToolCall[{Id}]: unreadable {name} {arguments}: {error}");
					if (firstError == null)
						firstError = $"[ERROR] The arguments of '{name}' could not be read: {error}\n";
					continue;
				}

				calls.Add(call);
				escaped.Add(arguments);
			}

			if (firstError == null)
				foreach (var call in calls)
				{	LLE.Log($"llmToolCall[{Id}]: {call.Text}");
					MyConsole.AddMultiline(call.Text + "\n", Color.Cyan);
				}

			var json = new StringBuilder("{\"role\":\"assistant\",\"content\":");
			Json.Quoted(json, content.ToString().Trim());

			if (firstError != null) return new Answer(calls, json.Append('}').ToString(), 0, firstError);

			int firstId = nextCallId;
			nextCallId += calls.Count;

			for (int i = 0; i < calls.Count; ++i)
			{
				json.Append(i == 0 ? ",\"tool_calls\":[" : ",");
				json.Append("{\"id\":");
				Json.Quoted(json, CallId(firstId + i));
				json.Append(",\"type\":\"function\",\"function\":{\"name\":");
				Json.Quoted(json, calls[i].Name);
				json.Append(",\"arguments\":\"").Append(escaped[i]).Append("\"}}");
			}

			if (calls.Count > 0) json.Append(']');
			json.Append('}');

			return new Answer(calls, json.ToString(), firstId, firstError);
		}

		private static int nextCallId;

		public static string CallId(int index)
		{	return "c" + index;
		}
	}
}
