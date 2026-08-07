namespace LLE
{
	// Two streams of the same model at two speeds. The fast one runs with thinking off and answers
	// in about a second; the deep one thinks and takes five or six. Both are given the same
	// question at the same time, and the fast answer decides which of them the turn belongs to:
	// anything that puts a blueprint into the world waits for the deep stream, everything else runs
	// on the fast one and the deep stream is cut off mid-sentence.
	//
	// The two are not peers and their agreement means nothing: the deep stream is the authority
	// where it is asked at all, and the fast one is a way not to wait for it everywhere else.
	//
	// Measured, same task, same stand: thinking everywhere 12.2 minutes, thinking nowhere 2.3 — and
	// the same 1.9 commands actually executed per turn either way. The difference is latency, not
	// work, which is why it is worth paying it only where it buys something.
	//
	// A stream missing from the loader config is not an error; the turn is then decided by whoever
	// is there.
	class Escalation
	{
		public const int Fast = 0; // thinking off in the loader config
		public const int Deep = 1; // thinking on
		public const int Count = 2;

		private readonly LlmChannel[] channels = new LlmChannel[Count];
		private readonly string[] answers = new string[Count];
		private readonly bool[] finished = new bool[Count];

		private string error;

		public Escalation()
		{	for (int i = 0; i < Count; ++i)
			{	channels[i] = new LlmChannel(i) { EchoToConsole = i == Fast };
				finished[i] = true;
			}
		}

		// The fast stream is the one the mod talks about: it is present in every configuration.
		public int ContextWindow { get { return channels[Fast].ContextWindow; } }

		public string Error { get { return error; } }

		// Both streams are given the same words: they are the same model, and the only thing that
		// differs between them is the thinking flag, which lives in the loader config.
		public static void SetSystemPromptAll(string text, string stop)
		{	for (int i = 0; i < Count; ++i)
				if (LLE_Loader.GetContextWindow(i) > 0)
					LLE_Loader.SetSystemPrompt(i, text, stop);
		}

		public void Send(string userText)
		{
			error = null;

			for (int i = 0; i < Count; ++i)
			{
				answers[i] = null;
				finished[i] = !channels[i].Present;
				if (!finished[i]) channels[i].Send(userText);
			}
		}

		public void Poll()
		{
			for (int i = 0; i < Count; ++i)
			{
				if (finished[i]) continue;

				string payload;
				switch (channels[i].Poll(out payload))
				{
					case ChannelEvent.Response:
						answers[i] = payload;
						finished[i] = true;
						break;

					case ChannelEvent.Error:
						error = payload;
						finished[i] = true;
						break;
				}
			}
		}

		public bool Finished(int stream) { return finished[stream]; }

		// null means the stream is absent, died, or was cut off before it said anything.
		public string Answer(int stream) { return answers[stream]; }

		// Its answer is not wanted this turn. The slot keeps its cached prefix, so the stream pays
		// nothing for being interrupted — measured: prompt_n=1 on its next request.
		public void Cancel(int stream)
		{
			if (finished[stream]) return;

			channels[stream].Cancel();
			finished[stream] = true;
		}
	}
}
