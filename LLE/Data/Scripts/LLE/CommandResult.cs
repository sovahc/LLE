namespace LLE
{
	// Null = command still running (coroutine in progress).
	// Success(msg) = success; plain string (implicit) = error.
	internal class CommandResult
	{
		public readonly bool Ok;
		public readonly string Message;

		internal CommandResult(bool ok, string message)
		{	Ok = ok;
			Message = message;
		}

		public static CommandResult Success(string message)
		{	return new CommandResult(true, message);
		}

		// Plain string = error by default (errors outnumber successes).
		public static implicit operator CommandResult(string message)
		{	return new CommandResult(false, message);
		}

		public override string ToString()
		{	return Message;
		}
	}
}
