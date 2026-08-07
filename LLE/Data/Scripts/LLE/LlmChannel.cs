using System.Text;

using VRageMath;

namespace LLE
{
	// What a finished poll gave us. None is the normal case — the answer is still coming.
	enum ChannelEvent { None, Response, Error }

	// One conversation with one model, seen from the mod side: what was sent, what came back,
	// and whether the channel is free. It knows nothing about commands, turns or the game —
	// that belongs to whoever owns the channel.
	//
	// Every channel has its own buffers and its own busy flag. Sharing either of them across
	// channels merges two streams into one answer and releases the wrong sender.
	class LlmChannel
	{
		public readonly int Id;

		// One channel streams into the console as it generates. There is one console, so the
		// others stay in the log until they have a console of their own.
		public bool EchoToConsole;

		private readonly StringBuilder reasoning = new StringBuilder();
		private readonly StringBuilder content = new StringBuilder();
		private readonly StringBuilder response = new StringBuilder();

		private MessageType lastType = MessageType.Stop;
		private bool busy;

		public LlmChannel(int id)
		{	Id = id;
		}

		// 0 means the loader has no such channel — the whole feature is simply absent.
		public int ContextWindow { get { return LLE_Loader.GetContextWindow(Id); } }
		public bool Present { get { return ContextWindow > 0; } }
		public bool Busy { get { return busy; } }

		public void SetSystemPrompt(string text, string stop)
		{	LLE_Loader.SetSystemPrompt(Id, text, stop);
		}

		// userText is the whole message; the loader keeps no history of its own.
		public void Send(string userText)
		{	LLE_Loader.SendMessageToLLM(Id, userText);
			busy = true;
		}

		// Stop generating: the answer is not needed this turn. The loader drops the connection and
		// throws away whatever this request had already queued, so nothing of it reaches the next
		// turn — but the server keeps the slot's cached prefix, so the next send is still cheap.
		public void Cancel()
		{
			if (!busy) return;

			LLE_Loader.CancelLLM(Id);
			busy = false;

			reasoning.Clear();
			content.Clear();
			response.Clear();
			lastType = MessageType.Stop;
		}

		public ChannelEvent Poll(out string payload)
		{
			payload = null;

			for (int i = 0; i < 10; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(Id, out m)) return ChannelEvent.None;

				// Type changed — log and clear the old buffer
				if (m.Type != lastType)
				{
					switch (lastType)
					{
						case MessageType.Reasoning:
							LLE.Log($"llmReasoning[{Id}]:\n{reasoning}");
							reasoning.Clear();
							break;
						case MessageType.Content:
							response.Append(content);
							response.Append("\n");

							LLE.Log($"llmContent[{Id}]:\n{content}");
							content.Clear();
							break;
					}
				}
				lastType = m.Type;

				switch (m.Type)
				{
					case MessageType.Reasoning:
						if (EchoToConsole) MyConsole.AddMultiline(m.Payload, Color.LightGray);
						reasoning.Append(m.Payload);
						break;

					case MessageType.Content:
						if (EchoToConsole) MyConsole.AddMultiline(m.Payload, Color.Cyan);
						content.Append(m.Payload);
						break;

					case MessageType.Stop:
						if (EchoToConsole) MyConsole.AddMultiline("\n", Color.White);

						busy = false;

						payload = response.Length == 0 ? "[EMPTY RESPONSE]" : response.ToString();
						response.Clear();
						return ChannelEvent.Response;

					case MessageType.Error:
						busy = false;

						payload = m.Payload;
						response.Clear();
						return ChannelEvent.Error;
				}
			}

			return ChannelEvent.None;
		}
	}
}
