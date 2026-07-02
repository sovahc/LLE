using System.Text;

using VRageMath;

namespace LLE
{
	class LLM
	{
		private void Log(string s) => LLE.Log(s);

		private readonly StringBuilder Reasoning = new StringBuilder(); // input
		private readonly StringBuilder Content = new StringBuilder(); // input
		private readonly StringBuilder commandToProcess = new StringBuilder();
		private readonly StringBuilder output = new StringBuilder();

		public bool pauseLLM;

		private MessageType lastType = MessageType.Stop;
		private bool waitingForResponse;

		private Commands commands;

		public LLM(Commands commands_)
		{	commands = commands_;
		}

		public void CommandResult(string r)
		{
			Append("\n[COMMAND RESULT]:\n", Color.Yellow);
			Append(r, Color.Green);
			Append("\n", Color.White);
		}

		public void Append(string text, Color color)
		{	MyConsole.AddMultiline(text, color);
			output.Append(text);
		}

		public void Tick()
		{
			// Send accumulated results to LLM
			if (output.Length != 0 && !pauseLLM && !waitingForResponse)
			{
				waitingForResponse = true;

				string m = output.ToString();
				output.Clear();

				Log($"toLLM: {m}");
				//MyConsole.AddMultiline(m, Color.Green);
				LLE_Loader.SendMessageToLLM(m);
				return;
			}

			// Poll for new chunks from LLM

			for (int i = 0; i < 10; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(out m)) return;
				
				// Type changed — log and clear the old buffer
				if (m.Type != lastType)
				{
					switch(lastType)
					{	case MessageType.Reasoning:
							Log($"llmReasoning:\n{Reasoning}");
							Reasoning.Clear();
							break;
						case MessageType.Content:
							commandToProcess.Append(Content);
							commandToProcess.Append("\n");

							Log($"llmContent:\n{Content}");
							Content.Clear();
							break;
					}

				}
				lastType = m.Type;

				if (m.Type == MessageType.Reasoning)
				{
					MyConsole.AddMultiline(m.Payload, Color.LightGray);
					Reasoning.Append(m.Payload);
				}
				else if (m.Type == MessageType.Content)
				{
					MyConsole.AddMultiline(m.Payload, Color.Cyan);
					Content.Append(m.Payload);
				}
				else if (m.Type == MessageType.Stop)
				{	waitingForResponse = false;

					MyConsole.AddMultiline("\n", Color.White);

					// LLM stopped sending — try to process accumulated content
					ProcessLlmContent(commandToProcess.ToString());
					commandToProcess.Clear();
					return;
				}
			}
		}

		private void ProcessLlmContent(string content)
		{
			string trimmed = content.Trim();
			const string prefix = "Execute `";

			// Find last line starting with 'Execute `' — weak models sometimes
			// echo context markers after the command.
			string lastLine = null;
			var lines = trimmed.Split('\n');
			for (int i = lines.Length - 1; i >= 0; --i)
			{
				string l = lines[i].Trim();
				if (l.StartsWith(prefix)) { lastLine = l; break; }
			}

			if (lastLine == null)
			{
				Log($"ProcessLlmContent ERROR dump: contentLength={content.Length} lastLine=[{lastLine}]\n---CONTENT START---\n{content}\n---CONTENT END---");
				CommandResult("ERROR: Last line must start with 'Execute `command`', e.g.: Execute `fly 1 2 3`");
				return;
			}

			int closingBacktick = lastLine.IndexOf('`', prefix.Length);
			if (closingBacktick < 0)
			{
				CommandResult("ERROR: Missing closing backtick in command.");
				return;
			}

			string command = lastLine.Substring(prefix.Length, closingBacktick - prefix.Length);

			if(command == "pause")
			{	pauseLLM = true;
				return;
			}

			output.Append(content);
			output.Append($"[LLM COMMAND]: {command}\n");

			string result = commands.Execute(command);
			CommandResult(result);
		}
	}
}