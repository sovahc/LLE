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

		public readonly string Error;

		public Answer(List<ToolCall> calls, string assistantJson, string error)
		{	Calls = calls;
			AssistantJson = assistantJson;
			Error = error;
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

		public LlmChannel(int id)
		{	Id = id;
		}

		public int ContextWindow { get { return LLE_Loader.GetContextWindow(Id); } }
		public bool Present { get { return ContextWindow > 0; } }
		public bool Busy { get { return busy; } }

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

			for (int i = 0; i < 10; ++i)
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

				if (at < 0 || at >= 64) continue; // the loop below grows the lists up to this number

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
					callArguments[at].Append(Unquote(arguments.Raw));
			}
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
			var escaped = new List<string>();   // the arguments of the calls above, as they arrived
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

			// After the loop, not inside it: one unreadable call drops the whole turn.
			if (firstError == null)
				foreach (var call in calls)
				{	LLE.Log($"llmToolCall[{Id}]: {call.Text}");
					MyConsole.AddMultiline(call.Text + "\n", Color.Cyan);
				}

			var json = new StringBuilder("{\"role\":\"assistant\",\"content\":");
			Json.Quoted(json, content.ToString().Trim());

			if (firstError != null) return new Answer(calls, json.Append('}').ToString(), firstError);

			for (int i = 0; i < calls.Count; ++i)
			{
				json.Append(i == 0 ? ",\"tool_calls\":[" : ",");
				json.Append("{\"id\":");
				Json.Quoted(json, CallId(i));
				json.Append(",\"type\":\"function\",\"function\":{\"name\":");
				Json.Quoted(json, calls[i].Name);
				json.Append(",\"arguments\":\"").Append(escaped[i]).Append("\"}}");
			}

			if (calls.Count > 0) json.Append(']');
			json.Append('}');

			return new Answer(calls, json.ToString(), firstError);
		}

		public static string CallId(int index)
		{	return "c" + index;
		}
	}
}
