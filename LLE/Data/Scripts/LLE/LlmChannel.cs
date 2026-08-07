using System.Collections.Generic;
using System.Text;

using VRageMath;

namespace LLE
{
	// What a finished poll gave us. None is the normal case — the answer is still coming.
	enum ChannelEvent { None, Response, Error }

	// One finished response: the calls to run, and the turn itself already written as the JSON the
	// next request will carry. The turn goes back into the conversation as that string and is never
	// rebuilt out of the calls — what came off the wire is what returns to it.
	class Answer
	{
		public readonly List<ToolCall> Calls;
		public readonly string AssistantJson;

		// Filled when a call came back unreadable. The turn keeps the calls it could read; this is
		// what the model is told about the one it could not.
		public readonly string Error;

		public Answer(List<ToolCall> calls, string assistantJson, string error)
		{	Calls = calls;
			AssistantJson = assistantJson;
			Error = error;
		}
	}

	// One conversation with one model, seen from the mod side: what was sent, what came back,
	// and whether the channel is free. It knows nothing about commands, turns or the game —
	// that belongs to whoever owns the channel.
	//
	// The stream is read here, not in the loader: the loader moves bytes and says when they stop.
	// Deciding what a chunk means on the side that acts on it is the whole point — the answer is
	// parsed one way only, and the pieces that go back into the next request go back untouched.
	//
	class LlmChannel
	{
		public readonly int Id;

		private readonly StringBuilder reasoning = new StringBuilder();
		private readonly StringBuilder content = new StringBuilder();

		// One entry per call the model is making, in the order the stream numbered them. Names and
		// arguments arrive in pieces and are concatenated, never interpreted on the way in.
		private readonly List<StringBuilder> callNames = new List<StringBuilder>();
		private readonly List<StringBuilder> callArguments = new List<StringBuilder>();

		private bool busy;

		public LlmChannel(int id)
		{	Id = id;
		}

		// 0 means the loader has no such channel — the whole feature is simply absent.
		public int ContextWindow { get { return LLE_Loader.GetContextWindow(Id); } }
		public bool Present { get { return ContextWindow > 0; } }
		public bool Busy { get { return busy; } }

		// requestJson is the `messages` and `tools` of the request, already written out. The loader
		// wraps it in the fields that belong to the endpoint and posts it; it keeps no history.
		public void Send(string requestJson)
		{	LLE_Loader.SendMessageToLLM(Id, requestJson);
			busy = true;
		}

		// Stop waiting for this answer. The loader abandons the request, and the half-finished text
		// in these buffers belongs to nobody — the next Send must not find it here.
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
		}

		public ChannelEvent Poll(out Answer answer, out string errorText)
		{
			answer = null;
			errorText = null;

			// A stream arrives as many small lines, so the budget per poll is generous. It is a
			// budget and not a loop to exhaustion: one channel must not hold the frame.
			for (int i = 0; i < 200; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(Id, out m)) return ChannelEvent.None;

				switch (m.Type)
				{
					case MessageType.Chunk:
						ReadChunk(m.Payload);
						break;

					case MessageType.Stop:
						MyConsole.AddMultiline("\n", Color.White);
						busy = false;
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

		// One `data:` line. Everything the mod needs is under choices[0].delta; anything else in
		// there belongs to the endpoint and is ignored rather than guessed at.
		private void ReadChunk(string data)
		{
			string error;
			var root = Json.Parse(data, out error);
			if (root == null)
			{	LLE.Log($"llmChunk[{Id}] unreadable: {error}: {data}");
				return;
			}

			var choices = root.Field("choices");
			if (choices == null || !choices.Is(JsonKind.Array) || choices.Array.Count == 0) return;

			var delta = choices.Array[0].Field("delta");
			if (delta == null) return;

			// Reasoning — the field name differs by endpoint:
			//   local (llama.cpp): "reasoning_content"
			//   openrouter:        "reasoning"
			var think = delta.Field("reasoning_content") ?? delta.Field("reasoning");
			if (think != null && think.Is(JsonKind.String) && think.String.Length != 0)
			{	MyConsole.AddMultiline(think.String, Color.LightGray);
				reasoning.Append(think.String);
			}

			var text = delta.Field("content");
			if (text != null && text.Is(JsonKind.String) && text.String.Length != 0)
			{	MyConsole.AddMultiline(text.String, Color.Cyan);
				content.Append(text.String);
			}

			var calls = delta.Field("tool_calls");
			if (calls == null || !calls.Is(JsonKind.Array)) return;

			foreach (var call in calls.Array)
			{
				var index = call.Field("index");
				int at = index != null && index.Is(JsonKind.Number) ? (int)index.Number : 0;

				while (callNames.Count <= at)
				{	callNames.Add(new StringBuilder());
					callArguments.Add(new StringBuilder());
				}

				var function = call.Field("function");
				if (function == null) continue;

				var name = function.Field("name");
				if (name != null && name.Is(JsonKind.String)) callNames[at].Append(name.String);

				// Raw, not the decoded value: this is the piece that goes back into the next
				// request, and it goes back written exactly as it arrived. Raw keeps the quotes,
				// so each fragment loses its own pair and the insides are joined.
				var arguments = function.Field("arguments");
				if (arguments != null && arguments.Is(JsonKind.String))
					callArguments[at].Append(Unquote(arguments.Raw));
			}
		}

		// A JSON string literal without its surrounding quotes. The escapes inside stay escaped.
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
			var escaped = new List<string>();   // the arguments of the calls above, as they arrived
			string firstError = null;

			for (int i = 0; i < callNames.Count; ++i)
			{
				var name = callNames[i].ToString();
				if (name.Length == 0) continue;

				var arguments = callArguments[i].ToString();
				if (arguments.Length == 0) arguments = "{}";

				// The arguments were kept escaped for the request; reading them needs the text
				// itself, which is the one place the two forms part company.
				string error;
				var text = Json.Unescape(arguments, out error);
				var call = text == null ? null : ToolCall.Parse(name, text, out error);

				if (call == null)
				{	LLE.Log($"llmToolCall[{Id}]: unreadable {name} {arguments}: {error}");
					if (firstError == null)
						firstError = $"[ERROR] The arguments of '{name}' could not be read: {error}\n";
					continue;
				}

				LLE.Log($"llmToolCall[{Id}]: {call.Text}");
				MyConsole.AddMultiline(call.Text + "\n", Color.Cyan);

				calls.Add(call);
				escaped.Add(arguments);
			}

			// The assistant turn, written once, in the shape the next request needs. Only the calls
			// that were understood go in: a call in the record with no result against it is a turn
			// the model cannot read back, and one it could not run has no result to give. Reasoning
			// stays out — the chat template drops thought from previous turns anyway.
			var json = new StringBuilder("{\"role\":\"assistant\",\"content\":");
			Json.Quoted(json, content.ToString().Trim());

			// A turn with an unreadable call is refused whole, so its calls never run and never get
			// a result. What goes on record is what the model said, and nothing it asked for.
			if (firstError != null) return new Answer(calls, json.Append('}').ToString(), firstError);

			for (int i = 0; i < calls.Count; ++i)
			{
				json.Append(i == 0 ? ",\"tool_calls\":[" : ",");
				json.Append("{\"id\":");
				Json.Quoted(json, CallId(i));
				json.Append(",\"type\":\"function\",\"function\":{\"name\":");
				Json.Quoted(json, calls[i].Name);
				// The arguments go back as the model's own bytes, quoted the way they arrived.
				json.Append(",\"arguments\":\"").Append(escaped[i]).Append("\"}}");
			}

			if (calls.Count > 0) json.Append(']');
			json.Append('}');

			return new Answer(calls, json.ToString(), firstError);
		}

		// The id a result must carry to answer the call at this position of the last turn.
		public static string CallId(int index)
		{	return "c" + index;
		}
	}
}
