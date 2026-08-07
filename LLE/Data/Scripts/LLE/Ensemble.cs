namespace LLE
{
	// Three identical local streams answering the same conversation at once. Where two of them
	// start alike, that is what the bot does; where all three part company, the streams are shown
	// the plans and asked to choose.
	//
	// Measured with two streams on a 30-turn session (720 samples, gemma-4-26B at the shipping
	// sampler): the streams pick the same first command on 72% of turns, and the turns where they
	// part are the same turns under a different system prompt (r = 0.80). Disagreement is a
	// property of the state, not of the sampler — which is what makes it worth acting on.
	//
	// A stream missing from the loader config is not an error: the turn runs with the streams that
	// are there and nothing else changes.
	class Ensemble
	{
		public const int Streams = 3;

		private readonly LlmChannel[] channels = new LlmChannel[Streams];
		private readonly string[] answers = new string[Streams];
		private readonly bool[] waiting = new bool[Streams];

		private string error;

		public Ensemble()
		{	for (int i = 0; i < Streams; ++i)
				channels[i] = new LlmChannel(i) { EchoToConsole = i == 0 };
		}

		// There is one console, so only the first stream is echoed into it. The other is in the log.
		public int ContextWindow { get { return channels[0].ContextWindow; } }

		public string Error { get { return error; } }

		public bool Busy
		{	get
			{	for (int i = 0; i < Streams; ++i)
					if (waiting[i]) return true;
				return false;
			}
		}

		// The streams are identical or they are not an ensemble, so the prompt goes to all of them.
		public static void SetSystemPromptAll(string text, string stop)
		{	for (int i = 0; i < Streams; ++i)
				if (LLE_Loader.GetContextWindow(i) > 0)
					LLE_Loader.SetSystemPrompt(i, text, stop);
		}

		public void Send(string userText)
		{
			error = null;

			for (int i = 0; i < Streams; ++i)
			{
				answers[i] = null;
				waiting[i] = channels[i].Present;
				if (waiting[i]) channels[i].Send(userText);
			}
		}

		// True once every stream that was sent to has finished. An entry is null for a stream that
		// is absent from the config or died on the way — the caller works with what came back.
		public bool Poll(out string[] result)
		{
			result = null;

			bool outstanding = false;

			for (int i = 0; i < Streams; ++i)
			{
				if (!waiting[i]) continue;
				outstanding = true;

				string payload;
				switch (channels[i].Poll(out payload))
				{
					case ChannelEvent.Response:
						answers[i] = payload;
						waiting[i] = false;
						break;

					case ChannelEvent.Error:
						error = payload;
						waiting[i] = false;
						break;
				}
			}

			if (!outstanding) return false;

			for (int i = 0; i < Streams; ++i)
				if (waiting[i]) return false;

			// A copy: the caller holds the answers across ticks, and the next Send clears these.
			result = new string[Streams];
			for (int i = 0; i < Streams; ++i) result[i] = answers[i];
			return true;
		}
	}
}
