using System;

using VRageMath;

namespace LLE
{
	// The second stream. It reads the executor's transcript and answers OK or PROBLEM; it never
	// issues a command and never touches `pause`. Its verdict goes to the console and the log
	// only — routing it back into the bot's own transcript is a separate decision.
	//
	// Absent channel, busy channel, dead network: all three are silence, and silence is a
	// correct state here. Nothing in the local loop waits for this.
	class Verifier
	{
		private readonly LlmChannel channel = new LlmChannel(LlmChannel.Verifier);

		private bool promptSet;
		private int sentTurn;
		private double sentAt; // 0 = nothing outstanding

		public void Kick(string conversation, int turn)
		{
			if (!channel.Present) return;

			if (channel.Busy)
			{	LLE.Log($"verifier: still busy at turn {turn}, skipped (sent at turn {sentTurn})");
				return;
			}

			if (!promptSet)
			{	channel.SetSystemPrompt(Prompts.Verifier, null);
				promptSet = true;
			}

			channel.Send(conversation);
			sentTurn = turn;
			sentAt = Time.Now;
		}

		public void Tick(int turn)
		{
			string payload;
			switch (channel.Poll(out payload))
			{
				case ChannelEvent.Response:
					Report(payload, turn);
					sentAt = 0;
					break;

				case ChannelEvent.Error:
					MyConsole.AddNewLine();
					MyConsole.Add($"[VERIFIER] channel error: {payload}", Color.OrangeRed);
					sentAt = 0;
					break;
			}
		}

		private void Report(string text, int turn)
		{
			// How late the verdict is, in turns and in seconds, is the whole point of the
			// experiment — it is printed even when the answer is a bare OK.
			string when = sentAt > 0
				? $"turn {sentTurn}, +{turn - sentTurn} turns, {Time.Now - sentAt:F1}s"
				: "unsolicited";

			text = text == null ? "" : text.Trim();

			// The executor is streaming into the same console; start on a fresh line so the
			// verdict does not land in the middle of its sentence.
			MyConsole.AddNewLine();

			// A verifier that said nothing at all is a broken channel, not a finding. Red is
			// reserved for what the bot did wrong.
			if (text.Length == 0 || text == "[EMPTY RESPONSE]")
			{	MyConsole.Add($"[VERIFIER] empty answer ({when})", Color.OrangeRed);
				return;
			}

			if (text.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
			{	MyConsole.Add($"[VERIFIER] OK ({when})", Color.DarkSeaGreen);
				return;
			}

			// Anything that is not OK is a problem report, including a verifier that lost the
			// protocol: a channel that cannot say OK has nothing useful to say quietly.
			MyConsole.Add($"[VERIFIER] PROBLEM ({when})", Color.Red);
			MyConsole.Add(text, Color.Red);
		}
	}
}
