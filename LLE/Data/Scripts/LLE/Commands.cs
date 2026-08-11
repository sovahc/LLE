using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using System.Linq;
using Sandbox.ModAPI;


// Stack-based coroutine runner:
//   yield return null;         = wait one tick
//   yield return Success(msg)  = final success response to LLM (terminates whole command)
//   yield return "error msg"   = final error response to LLM (terminates whole command)
//   yield return IEnumerator;  = run nested coroutine to completion, then resume parent
//   yield return Validated;    = everything above is a check, everything below changes the world
//   yield break;              = done at this level (parent resumes, or command ends)
// ! Re-query engine objects after `yield return null;` don't cache references.
// ! A top-level coroutine MUST end with a CommandResult (Success/Incomplete/string).
//   Falling off the end or `yield break` at top level is reported as Incomplete by Update().
//
// Validate() drives a command to its first yield and stops: `Validated` means the checks passed,
// a string means they did not. It never reaches the world-changing half, so nothing above the
// `Validated` of a command may change anything — it is run twice, once to check and once for real.
// A command without a `Validated` passes unchecked.
//
// Design note: `yield return "error msg"` without a trailing `yield break;` works because
// Commands.Update() disposes the entire coroutine stack the moment it receives a string.
// The code after such a yield never executes — this is intentional: it avoids a redundant
// `yield break;` after every error path, keeping the coroutine bodies compact.

namespace LLE
{
	public partial class Commands
	{
		private const string IE_NO_INVENTORY = "Internal error: character.GetInventory() is null";
		private const string E_BAD_POINT = "Error: You are not at the correct interaction point with the block.";

		internal static CommandResult Success(string message) => CommandResult.Success(message);
		internal static CommandResult Incomplete(string message) => CommandResult.Incomplete(message);

		internal static readonly Validation Validated = new Validation();

		private static readonly MyDefinitionId hydrogenId =
			new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
		private static readonly MyDefinitionId electricityId =
			new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

		private IMyCubeGrid selectedGrid;
		private MyVoxelBase selectedAsteroid;

		private readonly IMyCharacter character;

		private Status status;

		internal string Status_ReportChanged() => status.ReportChanged();
		internal void Status_Tick() => status.Tick();

		private readonly Stack<IEnumerator> coroutineStack = new Stack<IEnumerator>();

		private readonly Dictionary<string, string> memory = new Dictionary<string, string>();

		private double resumeTime;

		private void SetPause(double time)
		{	resumeTime = Time.Now + time;
		}
		private bool IsPaused()
		{	return Time.Now < resumeTime;			
		}

		public Commands(IMyCharacter character_)
		{
			character = character_;
			status = new Status(character);

			ALL_COMPONENTS.Clear();
			foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyPhysicalItemDefinition>())
			{
				if (def.Id.TypeId == typeof(MyObjectBuilder_Component))
					ALL_COMPONENTS.Add(def.Id.SubtypeName);
			}
		}

		public Vector3D GetEngineerCenter()
		{
			return character.GetPosition() + Constants.EngineerHeight/2 * character.WorldMatrix.Up;
		}

		private MyEntity3DSoundEmitter soundEmitter;

		private void PlaySound(string sound)
		{
			if (soundEmitter == null)
			{
				soundEmitter = new MyEntity3DSoundEmitter(character as MyEntity);
			}
			if (soundEmitter != null)
			{
				soundEmitter.PlaySound(new MySoundPair(sound));
			}
		}

		private void StopSound()
		{	if (soundEmitter == null) return;
			soundEmitter.StopSound(false);
		}

		private static bool Include(string searchTerm, string text)
		{	if(string.IsNullOrEmpty(searchTerm) || searchTerm == "*") return true;
			return text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private string MyError(Vector3D engineer, string query, List<IMyEntity> matches)
		{
			if(matches.Count == 0)
				return $"Error: object '{query}' not found. Use the exact object name.";

			StringBuilder sb = new StringBuilder();
			sb.Append($"Error: multiple objects match '{query}':\n");
			foreach (var e in matches)
			{
				string category, name;
				Description(e, out category, out name);
				double distance = (e.WorldMatrix.Translation - engineer).Length();
				sb.Append($"* {category} {Quote(name)} → {Distance(distance)}\n");
			}
			sb.Append("\n\n");
			return sb.ToString();
		}

		internal IEnumerator Pause()
		{
			yield return Validated;

			LLM.pause = true;
			yield return Success("OK");
		}

		internal IEnumerator Say(ToolCall call)
		{
			var message = call.Str("message");
			if (string.IsNullOrEmpty(message))
				yield return call.Need("message");

			yield return Validated;

			MyVisualScriptLogicProvider.SendChatMessage(
				message, character.DisplayName, character.ControllerInfo.ControllingIdentityId, "Yellow");
			LLE_Loader.Speak(message);
			yield return Success("Done");
		}

		// The note is the call itself: written into the transcript, it stays in front of the model.
		// Reasoning does not — it never reaches the assistant message. Nothing is stored on this side.
		internal CommandResult Note(ToolCall call)
		{
			var text = call.Str("text");
			if (string.IsNullOrEmpty(text)) return call.Need("text");

			return Success("Noted.");
		}

		internal IEnumerator Memory(ToolCall call)
		{
			var key = call.Str("key");
			var value = call.Str("value");
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
				yield return "Error: memory needs both a key and a value.";

			yield return Validated;

			memory[key] = value;
			yield return Success("Saved.");
		}

		internal string SystemPrompt { get; private set; }
		internal int SystemPromptChars { get; private set; }

		internal void SetSystemPromptAndMemory()
		{
			var sb = new StringBuilder(Prompts.Executor);
			sb.Append("\n## MEMORY\n");
			if (memory.Count > 0)
			{
				foreach (var kv in memory)
					sb.Append("* ").Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
			}
			else
			{
				sb.Append("-- none --\n");
			}

			SystemPrompt = sb.ToString();
			SystemPromptChars = SystemPrompt.Length;
		}

		internal IEnumerator Select(ToolCall call)
		{
			var what = call.Str("name");
			if (string.IsNullOrEmpty(what)) yield return call.Need("name");

			var engineer = GetEngineerCenter();
			
			BoundingSphereD S = new BoundingSphereD(engineer, Constants.NearInformationRadius);
			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref S);

			List<IMyEntity> matches = new List<IMyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Description(e, out category, out name);

				if(Include(what, name) || Include(what, category)) matches.Add(e);
			}

			if(matches.Count != 1) yield return MyError(engineer, what, matches);

			var match = matches[0];

			Description(match, out category, out name);

			var grid = match as IMyCubeGrid;
			if(grid != null)
			{
				yield return Validated;

				Debug.Start(grid);
				selectedGrid = grid;
				selectedAsteroid = null;
				yield return Success($"Selected {category} {Quote(name)}\nGrid directions: {GridDirections(grid)}");
			}

			var asteroid = match as MyVoxelBase;
			if(asteroid != null)
			{	yield return "Error: Operations on asteroids are not supported yet.";

				//selectedGrid = null;
				//selectedAsteroid = asteroid;
				//return Success($"Selected {category} {Quote(name)}");
			}

			yield return $"Error: you can't select {category} '{name}'";
		}

		internal bool GridIsSet(out string message)
		{	if(selectedGrid == null)
			{	message = "Error: you should select a grid first. Use `select name`";
				return false;
			}
			message = null;
			return true;
		}

		public static bool IsProjection(IMyCubeGrid grid)
		{
			var mcg = grid as MyCubeGrid;
			return mcg != null && mcg.Projector != null;
		}

		internal bool CurrentGridIsProjection(out string message)
		{	if(IsProjection(selectedGrid))
			{	message = "Error: selected grid is a projection preview, not a built object. Not supported for this command.";
				return true;
			}
			message = null;
			return false;
		}

		internal Vector3I NearestToEngineer(List<Vector3I> list)
		{	Vector3D ec = GetEngineerCenter();

			var minimalDistanceSq = double.MaxValue;
			var nearest = Vector3I.Zero;

			foreach (var ijk in list)
			{	var world = selectedGrid.GridIntegerToWorld(ijk);
				var dsq = (ec - world).LengthSquared();

				if(dsq < minimalDistanceSq)
				{	minimalDistanceSq = dsq;
					nearest = ijk;
				}
			}
			return nearest;
		}

		internal void AppendList(List<Vector3I> list, StringBuilder sb)
		{	
			sb.Append("(");
			int added = 0;

			foreach (var ijk in list)
			{	var block = selectedGrid.GetCubeBlock(ijk);
				
				if(added != 0) sb.Append("; ");
				sb.Append(IJK(ijk));
				++added;
			}

			sb.Append(")");

			if(added >= 2)
			{	sb.Append($" (Nearest is {IJK(NearestToEngineer(list))})");
			}

			sb.Append("\n");
		}

		internal void AppendInteractionPoints(Vector3I ijk, StringBuilder sb)
		{	
			var block = selectedGrid.GetCubeBlock(ijk);

			var eqsr = new List<EQSResult>();
			int totalCount = 0;

			EQS.Query(block, GetEngineerCenter(), InteractionKind.Inventory, eqsr, 10);
			totalCount += eqsr.Count;

			if(eqsr.Count != 0)
			{	sb.Append("* Get/Put: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}

			EQS.Query(block, GetEngineerCenter(), InteractionKind.Recharge, eqsr, 10);
			totalCount += eqsr.Count;

			if(eqsr.Count != 0)
			{	sb.Append("* Recharge: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}
			
			EQS.Query(block, GetEngineerCenter(), InteractionKind.GrindWeld, eqsr, 10);
			totalCount += eqsr.Count;
			
			if(eqsr.Count != 0)
			{	sb.Append("* Grind/Weld: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}

			if(totalCount == 0)
			{	sb.Append("-- none --\n");
				sb.Append("(the block is likely fully obstructed by other blocks or rock)\n");
			}
		}

		internal bool IsAtInteractionPoint(IMySlimBlock block, InteractionKind kind, out string message)
		{
			var ec = GetEngineerCenter();
			var r = GetInteractionPointAt(block, kind, ec);
			if(r.HasValue)
			{	message = null;
				return true;
			}
			message = E_BAD_POINT;
			return false;
		}

		EQSResult? GetInteractionPointAt(IMySlimBlock block, InteractionKind kind, Vector3D point)
		{	var eqsr = new List<EQSResult>();
			var cell = block.CubeGrid.WorldToGridInteger(point);
			EQS.QueryOneCell(block, cell, GetEngineerCenter(), kind, eqsr, 1);
			if(eqsr.Count == 0) return null;

			return eqsr[0];
		}

		internal bool IsAtPoint(IMySlimBlock block, List<Vector3I> ip, out string message)
		{
			var engineerCell = selectedGrid.WorldToGridInteger(GetEngineerCenter());

			if(ip.Contains(engineerCell))
			{	message = null;
				return true;
			}

			message = E_BAD_POINT;
			return false;
		}

		internal bool InProgress()
		{	return coroutineStack.Count > 0;
		}

		internal void AbortCommand()
		{	foreach(var c in coroutineStack) (c as IDisposable)?.Dispose();
			coroutineStack.Clear();
		}

		internal CommandResult Update()
		{
			if (coroutineStack.Count == 0) return null;

			var top = coroutineStack.Peek();

			while (top.MoveNext())
			{
				var current = top.Current;
				if(current == Validated) continue;

				var result = current as CommandResult;
				if(result == null)
				{	var s = current as string;
					if(s != null) result = s;
				}

				if(result != null)
				{
					AbortCommand();
					return result;
				}

				var nested = current as IEnumerator;
				if(nested != null)
					coroutineStack.Push(nested);

				return null;
			}

			(top as IDisposable)?.Dispose();

			coroutineStack.Pop();

			// Without an answer here the batch head is never dequeued and the command re-runs forever.
			if(coroutineStack.Count == 0)
			{	MyConsole.Add("!yield break!", Color.DarkRed);
				return Incomplete("Command stopped early.");
			}

			return null;
		}

		internal CommandResult Execute(ToolCall call)
		{
			var instant = Instant(call);
			if(instant != null) return instant;

			var coroutine = Coroutine(call);
			if(coroutine == null) return NoSuchTool(call);

			coroutineStack.Push(coroutine);
			return null;
		}

		// Every command answered here is a pure read: Validate() checks it by running it.
		private CommandResult Instant(ToolCall call)
		{
			switch(call.Name)
			{
				case "note":           return Note(call);
				case "position":       return Position();
				case "overview":       return Overview();
				case "integrity":      return Integrity();
				case "projection":     return Projection();
				case "status":         return Success(status.ReportAll());
				case "inventories":    return Inventories();

				case "near":           return Near(call);
				case "free":           return Near(call, true);
				case "inventory":      return Inventory();
				case "inventory_block":return InventoryBlock(call);
				case "search":         return Search(call);
				case "distance":       return Distance(call, false);
				case "distance_between": return Distance(call, true);
				case "points":         return Points(call);
				case "info":           return Info(call);
				case "recharge_list":  return GetRechargePoints();
				case "draft_show":     return DraftShow();
			}

			return null;
		}

		private IEnumerator Coroutine(ToolCall call)
		{
			switch(call.Name)
			{
				case "pause":          return Pause();
				case "say":            return Say(call);
				case "memory":         return Memory(call);
				case "select":         return Select(call);
				case "enter":          return Enter(call);
				case "exit":           return Exit();

				case "draft":          return Draft(call);
				case "draft_conveyor": return DraftConveyor(call);
				case "draft_undo":     return DraftUndo();
				case "draft_clear":    return DraftClear();

				case "fly":            return Fly(call);
				case "fly_direction":  return Fly_Direction_N(call);
				case "approach":       return Approach(call);
				case "grind":          return Grind(call);
				case "weld":           return Weld(call);
				case "get":            return Get(call);
				case "put":            return Put(call, false);
				case "put_all_components": return Put(call, true);
				case "transfer":       return Transfer(call, false);
				case "transfer_all":   return Transfer(call, true);
				case "place":          return Place(call);
				case "place_conveyor": return PlaceConveyor(call);
				case "build":          return Build();
				case "route":          return Route(call);
				case "recharge":       return Recharge(call);
			}

			return null;
		}

		private static string NoSuchTool(ToolCall call)
		{	return $"Error: there is no tool called '{call.Name}'.";
		}

		// Null = the command would start. Anything else is the refusal it would answer with.
		internal string Validate(ToolCall call)
		{
			var instant = Instant(call);
			if(instant != null) return instant.Status == CommandStatus.Error ? instant.Message : null;

			var coroutine = Coroutine(call);
			if(coroutine == null) return NoSuchTool(call);

			var stack = new Stack<IEnumerator>();
			stack.Push(coroutine);

			try
			{
				while(stack.Count > 0)
				{
					var top = stack.Peek();

					if(!top.MoveNext())
					{	(top as IDisposable)?.Dispose();
						stack.Pop();
						continue;
					}

					var current = top.Current;
					if(current == Validated) return null;

					var error = current as string;
					if(error != null) return error;

					var result = current as CommandResult;
					if(result != null) return result.Status == CommandStatus.Error ? result.Message : null;

					var nested = current as IEnumerator;
					if(nested == null) return null;

					stack.Push(nested);
				}

				return null;
			}
			finally
			{	foreach(var c in stack) (c as IDisposable)?.Dispose();
			}
		}
	}
}
