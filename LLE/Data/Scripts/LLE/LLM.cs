using System.Collections.Generic;
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

		// Unified command queue state
		private Queue<string> pendingCommands = new Queue<string>();
		private StringBuilder batchResults = new StringBuilder();
		private string currentCommand = null;

		public LLM(Commands commands_)
		{	commands = commands_;
		}

		public void OnCommandFinished(string result)
		{
			batchResults.Append($"→ {currentCommand}: {result}\n");
			currentCommand = null;
			RunNextPending();
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

			// Collect only the trailing block of Execute commands (bottom-up).
			// This prevents commands embedded in reasoning/examples from being executed.
			var lines = trimmed.Split('\n');
			List<string> cmds = new List<string>();

			for (int i = lines.Length - 1; i >= 0; --i)
			{
				string l = lines[i].Trim();
				if (!l.StartsWith(prefix)) break;

				int closingBacktick = l.IndexOf('`', prefix.Length);
				if (closingBacktick < 0)
				{
					Log($"ProcessLlmContent ERROR: Missing closing backtick in line: {l}");
					break;
				}

				cmds.Add(l.Substring(prefix.Length, closingBacktick - prefix.Length));
			}

			// Reverse back to original order (first command first)
			cmds.Reverse();

			if (cmds.Count == 0)
			{
				OnCommandFinished("ERROR: No commands found. Use 'Execute `command`' on separate lines.");
				return;
			}

			if (cmds.Count == 1 && cmds[0] == "pause")
			{
				pauseLLM = true;
				return;
			}

			// Queue commands and start execution
			output.Append(content);
			batchResults.Clear();
			pendingCommands.Clear();
			foreach (var c in cmds) pendingCommands.Enqueue(c);

			RunNextPending();
		}

		private void RunNextPending()
		{
			if (pendingCommands.Count == 0)
			{
				// All commands executed. Flush results to output for LLM.
				if (batchResults.Length > 0)
					output.Append(batchResults);
				batchResults.Clear();
				return;
			}

			string cmd = pendingCommands.Dequeue();
			currentCommand = cmd;

			output.Append($"[LLM COMMAND]: {cmd}\n");

			string result = commands.Execute(cmd);
			if (result != null)
			{
				// Synchronous command — continue immediately
				batchResults.Append($"→ {cmd}: {result}\n");
				currentCommand = null;
				RunNextPending();
			}
			// If result == null, it's async. LLE.cs will call OnCommandFinished when done.
		}
	}
}