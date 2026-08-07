using System.Collections.Generic;

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
		// Temporarily 1: a single stream runs its own batch as it wrote it, with nothing to compare
		// against. Back to 3 restores the vote and the choice round — nothing else changes.
		public const int Streams = 1;

		private readonly LlmChannel[] channels = new LlmChannel[Streams];
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

		// The whole request travels on every send — the loader holds none of it.
		public void Send(string requestJson)
		{
			error = null;

			for (int i = 0; i < Streams; ++i)
			{
				waiting[i] = channels[i].Present;
				if (waiting[i]) channels[i].Send(requestJson);
			}
		}

		// One finished stream per call, in whatever order they come back; -1 when there is nothing
		// new. 'answer' is null for a stream that died on the way — Error carries what it said.
		public int Poll(out Answer answer)
		{
			answer = null;

			for (int i = 0; i < Streams; ++i)
			{
				if (!waiting[i]) continue;

				Answer payload;
				string errorText;

				switch (channels[i].Poll(out payload, out errorText))
				{
					case ChannelEvent.Response:
						waiting[i] = false;
						answer = payload;
						return i;

					case ChannelEvent.Error:
						waiting[i] = false;
						error = errorText;
						return i;
				}
			}

			return -1;
		}

		// The turn is settled and whoever is still generating is generating for nobody. A stream
		// that hallucinates its way into a loop costs the turn nothing once this is called.
		public void CancelOutstanding()
		{
			for (int i = 0; i < Streams; ++i)
			{
				if (!waiting[i]) continue;

				waiting[i] = false;
				channels[i].Cancel();
			}
		}
	}
}
