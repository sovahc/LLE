namespace LLE
{
	internal enum CommandStatus { Success, Incomplete, Error }

	// Null = command still running (coroutine in progress).
	// Success(msg) = done; Incomplete(msg) = stopped early (e.g. inventory full, items not found) — not an error;
	// plain string (implicit) = error.
	internal class CommandResult
	{
		public readonly CommandStatus Status;
		public readonly string Message;

		internal CommandResult(CommandStatus status, string message)
		{	Status = status;
			Message = message;
		}

		public static CommandResult Success(string message)
		{	return new CommandResult(CommandStatus.Success, message);
		}

		public static CommandResult Incomplete(string message)
		{	return new CommandResult(CommandStatus.Incomplete, message);
		}

		// Plain string = error by default (errors outnumber successes).
		public static implicit operator CommandResult(string message)
		{	return new CommandResult(CommandStatus.Error, message);
		}

		public override string ToString()
		{	return Message;
		}
	}
}
