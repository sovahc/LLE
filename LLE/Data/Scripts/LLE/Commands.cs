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
//   yield break;              = done at this level (parent resumes, or command ends)
// ! Re-query engine objects after `yield return null;` don't cache references.
// ! A top-level coroutine MUST end with a CommandResult (Success/Incomplete/string).
//   Falling off the end or `yield break` at top level is reported as Incomplete by Update().
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

		internal CommandResult Say(ToolCall call)
		{
			var message = call.Str("message");
			if (string.IsNullOrEmpty(message))
				return call.Need("message");

			MyVisualScriptLogicProvider.SendChatMessage(
				message, character.DisplayName, character.ControllerInfo.ControllingIdentityId, "Yellow");
			return Success("Done");
		}

		internal CommandResult Memory(ToolCall call)
		{
			var key = call.Str("key");
			var value = call.Str("value");
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
				return "Error: memory needs both a key and a value.";
			memory[key] = value;
			return Success("Saved.");
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

		internal CommandResult Select(ToolCall call)
		{
			var what = call.Str("name");
			if (string.IsNullOrEmpty(what)) return call.Need("name");

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

			if(matches.Count != 1) return MyError(engineer, what, matches);

			var match = matches[0];

			Description(match, out category, out name);

			var grid = match as IMyCubeGrid;
			if(grid != null)
			{	
				Debug.Start(grid);
				selectedGrid = grid;
				selectedAsteroid = null;
				return Success($"Selected {category} {Quote(name)}\nGrid directions: {GridDirections(grid)}");
			}

			var asteroid = match as MyVoxelBase;
			if(asteroid != null)
			{	return "Error: Operations on asteroids are not supported yet.";
				
				//selectedGrid = null;
				//selectedAsteroid = asteroid;
				//return Success($"Selected {category} {Quote(name)}");
			}
			
			return $"Error: you can't select {category} '{name}'";
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

			if (top.MoveNext())
			{
				var current = top.Current;

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
			}
			else
			{
				(top as IDisposable)?.Dispose();

				coroutineStack.Pop();

				// Without an answer here the batch head is never dequeued and the command re-runs forever.
				if(coroutineStack.Count == 0)
				{	MyConsole.Add("!yield break!", Color.DarkRed);
					return Incomplete("Command stopped early.");
				}
			}
			return null;
		}

		internal CommandResult Execute(ToolCall call)
		{
			switch(call.Name)
			{
				case "pause":
					LLM.pause = true;
					return Success("OK");

				case "position":       return Position();
				case "overview":       return Overview();
				case "integrity":      return Integrity();
				case "projection":     return Projection();
				case "status":         return Success(status.ReportAll());
				case "inventories":    return Inventories();

				case "select":         return Select(call);
				case "say":            return Say(call);
				case "memory":         return Memory(call);
				case "near":           return Near(call);
				case "free":           return Near(call, true);
				case "inventory":      return Inventory();
				case "inventory_block":return InventoryBlock(call);
				case "search":         return Search(call);
				case "distance":       return Distance(call, false);
				case "distance_between": return Distance(call, true);
				case "points":         return Points(call);
				case "info":           return Info(call);
				case "enter":          return Enter(call);
				case "exit":           return Exit();
				case "recharge_list":  return GetRechargePoints();

				case "draft":          return Draft(call);
				case "draft_conveyor": return DraftConveyor(call);
				case "draft_show":     return DraftShow();
				case "draft_undo":     return DraftUndo();
				case "draft_clear":    return DraftClear();
			}

			IEnumerator coroutine;

			switch(call.Name)
			{
				case "fly":            coroutine = Fly(call); break;
				case "fly_direction":  coroutine = Fly_Direction_N(call); break;
				case "approach":       coroutine = Approach(call); break;
				case "grind":          coroutine = Grind(call); break;
				case "weld":           coroutine = Weld(call); break;
				case "get":            coroutine = Get(call); break;
				case "put":            coroutine = Put(call, false); break;
				case "put_all_components": coroutine = Put(call, true); break;
				case "transfer":       coroutine = Transfer(call, false); break;
				case "transfer_all":   coroutine = Transfer(call, true); break;
				case "place":          coroutine = Place(call); break;
				case "place_conveyor": coroutine = PlaceConveyor(call); break;
				case "build":          coroutine = Build(); break;
				case "route":          coroutine = Route(call); break;
				case "recharge":       coroutine = Recharge(call); break;

				default:
					return $"Error: there is no tool called '{call.Name}'.";
			}

			coroutineStack.Push(coroutine);
			return null;
		}
	}
}
