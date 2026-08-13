using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace LLE
{
	public partial class Commands
	{

		internal const string DraftGridName = "LLE_Draft";

		private struct DraftBlock
		{
			public MyCubeBlockDefinition Definition;
			public Vector3I Cell;
			public Base6Directions.Direction Forward;
			public Base6Directions.Direction Up;
		}

		private readonly List<DraftBlock> draft = new List<DraftBlock>();

		private IMyCubeGrid draftBase;
		private IMyCubeGrid draftPreview;
		private double draftSettleAt;

		// One `draft` per tick would spawn and close the ghost entity thirty times in half a second.
		private void TouchDraft()
		{	draftSettleAt = Time.Now + 0.3;
		}

		internal void Draft_Tick()
		{
			if(draftSettleAt != 0 && Time.Now >= draftSettleAt)
			{	draftSettleAt = 0;
				RebuildDraftPreview();
			}

			if(draftPreview == null || draftPreview.Closed) return;
			if(draftBase == null || draftBase.Closed) return;

			if(draftPreview.WorldMatrix != draftBase.WorldMatrix)
				draftPreview.WorldMatrix = draftBase.WorldMatrix;
		}

		private void ClearDraft()
		{	draft.Clear();
			draftBase = null;
			draftSettleAt = 0;
			CloseDraftPreview();
		}

		private void CloseDraftPreview()
		{	if(draftPreview == null) return;
			if(!draftPreview.Closed) draftPreview.Close();
			draftPreview = null;
		}

		private void RebuildDraftPreview()
		{
			CloseDraftPreview();

			if(draft.Count == 0) return;
			if(draftBase == null || draftBase.Closed) return;

			var gridOB = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
			gridOB.EntityId = 0;
			gridOB.DisplayName = DraftGridName;
			gridOB.GridSizeEnum = draftBase.GridSizeEnum;
			gridOB.CreatePhysics = false;
			gridOB.IsStatic = true;
			gridOB.Editable = false;
			gridOB.DestructibleBlocks = false;
			gridOB.IsRespawnGrid = false;
			gridOB.PersistentFlags = MyPersistentEntityFlags2.InScene;

			var m = draftBase.WorldMatrix;
			gridOB.PositionAndOrientation = new MyPositionAndOrientation(
				m.Translation, (Vector3)m.Forward, (Vector3)m.Up);

			foreach(var d in draft)
			{
				var ob = MyObjectBuilderSerializer.CreateNewObject(d.Definition.Id) as MyObjectBuilder_CubeBlock;
				if(ob == null) continue;

				ob.EntityId = 0;
				ob.Min = d.Cell;
				ob.BlockOrientation = new SerializableBlockOrientation(d.Forward, d.Up);
				gridOB.CubeBlocks.Add(ob);
			}

			var grid = MyAPIGateway.Entities.CreateFromObjectBuilderParallel(
				gridOB, true, OnDraftPreviewReady) as MyCubeGrid;
			if(grid == null) return;

			grid.IsPreview = true;
			grid.Save = false;

			draftPreview = grid;
		}

		// Touching a spawned grid before it finishes initializing crashes on some block types.
		private void OnDraftPreviewReady(IMyEntity entity)
		{
			var grid = entity as MyCubeGrid;
			if(grid == null || grid.Closed) return;

			// Out of the pruning structure, or the ghost reaches `select`, Vision and `search`.
			if(grid.TopMostPruningProxyId != -1) MyGamePruningStructure.Remove(grid);

			// Negated on purpose: negative dithering is what switches the shader to the projection
			// look, a positive one only fades the block out. See MyProjectorBase.SetTransparency.
			float transparency = -MyGridConstants.PROJECTOR_TRANSPARENCY;
			grid.Render.Transparency = transparency;

			var blocks = new List<IMySlimBlock>();
			((IMyCubeGrid)grid).GetBlocks(blocks);

			foreach(var b in blocks)
			{	b.Dithering = transparency;
				b.UpdateVisual();
			}
		}

		private bool FindInDraft(Vector3I ijk, out int index)
		{
			for(index = 0; index < draft.Count; ++index)
				if(draft[index].Cell == ijk) return true;

			index = -1;
			return false;
		}

		private string DraftGridMismatch()
		{
			if(draft.Count == 0 || draftBase == selectedGrid) return null;

			return $"Error: the draft belongs to {Quote(Name(draftBase))}, but {Quote(Name(selectedGrid))}"
				+ " is selected. Select that grid again, or drop the draft with `draft clear`.";
		}

		private void AddToDraft(MyCubeBlockDefinition definition, Vector3I ijk,
			Base6Directions.Direction forward, Base6Directions.Direction up)
		{
			draftBase = selectedGrid;
			draft.Add(new DraftBlock
			{	Definition = definition,
				Cell = ijk,
				Forward = forward,
				Up = up,
			});
			TouchDraft();
		}

		private bool TryGetDraftBlock(Vector3I ijk, out DraftBlock block)
		{
			int index;
			if(draftBase == selectedGrid && FindInDraft(ijk, out index))
			{	block = draft[index];
				return true;
			}

			block = default(DraftBlock);
			return false;
		}

		private void DraftCells(List<Vector3I> result)
		{
			result.Clear();
			if(draftBase != selectedGrid) return;

			foreach(var d in draft) result.Add(d.Cell);
		}

		private string CheckDraftSite(Vector3I ijk)
		{
			var occupant = selectedGrid.GetCubeBlock(ijk);
			if(occupant != null)
				return $"Error: {IJK(ijk)} is not empty — {Quote(Name(occupant))} stands there.";

			int index;
			if(FindInDraft(ijk, out index))
				return $"Error: {IJK(ijk)} is already in the draft — {Quote(draft[index].Definition.DisplayNameText)}.";

			foreach(var offset in Constants.SixDirections)
			{	if(selectedGrid.GetCubeBlock(ijk + offset) != null) return null;
				if(FindInDraft(ijk + offset, out index)) return null;
			}

			return $"Error: nothing touches {IJK(ijk)} by a face — neither a built block nor a drafted one."
				+ " The structure has to grow out of what is already there.";
		}

		internal IEnumerator DraftClear()
		{
			if(draft.Count == 0) yield return Success("The draft is already empty.");

			yield return Validated;

			int dropped = draft.Count;
			ClearDraft();
			yield return Success($"Draft cleared, {dropped} block(s) dropped.");
		}

		internal IEnumerator DraftUndo()
		{
			if(draft.Count == 0) yield return "Error: the draft is empty, nothing to undo.";

			yield return Validated;

			var last = draft[draft.Count - 1];
			draft.RemoveAt(draft.Count - 1);
			if(draft.Count == 0) ClearDraft(); else TouchDraft();

			yield return Success($"Removed {Quote(last.Definition.DisplayNameText)} at {IJK(last.Cell)}"
				+ $" from the draft. {draft.Count} block(s) left.");
		}

		internal IEnumerator Draft(ToolCall call)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			var mismatch = DraftGridMismatch();
			if(mismatch != null) yield return mismatch;

			var query = call.Str("type");
			if(string.IsNullOrEmpty(query))
				yield return call.Need("type");

			Vector3I ijk;
			if(!call.Ijk(out ijk))
				yield return call.NeedIjk;

			var refusal = CheckDraftSite(ijk);
			if(refusal != null) yield return refusal;

			Base6Directions.Direction forward, up;
			refusal = ParseFacing(call, out forward, out up);
			if(refusal != null) yield return refusal;

			string error;
			var definition = ResolvePlaceable(query, out error);
			if(definition == null) yield return error;

			refusal = RefuseTubeByName(definition);
			if(refusal != null) yield return refusal;

			yield return Validated;

			AddToDraft(definition, ijk, forward, up);

			yield return Success($"Drafted {Quote(definition.DisplayNameText)} at {IJK(ijk)}."
				+ $" {draft.Count} block(s) in the draft.");
		}

		private CommandResult DraftShow()
		{
			if(draft.Count == 0)
				return Success("The draft is empty. Add blocks with `draft 'Block Name' at I J K`.");

			var md = new MyMarkdown();
			md.Append($"# Draft on {Quote(Name(draftBase))} — {draft.Count} block(s), none built yet");

			var order = new List<string>();
			var cells = new Dictionary<string, StringBuilder>();

			foreach(var d in draft)
			{
				var key = $"{Quote(d.Definition.DisplayNameText)} facing {d.Forward.ToString().ToLowerInvariant()}"
					+ $" {d.Up.ToString().ToLowerInvariant()}";

				StringBuilder sb;
				if(!cells.TryGetValue(key, out sb))
				{	sb = new StringBuilder();
					cells[key] = sb;
					order.Add(key);
				}
				else sb.Append("; ");

				sb.Append(IJK(d.Cell));
			}

			foreach(var key in order)
				md.Append($"* {key} at ({cells[key]})");

			md.Append("The player sees this as a projection. Get his confirmation, then `build`.");
			return Success(md.Result());
		}

		internal IEnumerator Build()
		{
			string message;
			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			if(draft.Count == 0)
				yield return "Error: the draft is empty. Add blocks with `draft 'Block Name' at I J K` first.";

			if(draftBase != selectedGrid)
				yield return $"Error: the draft belongs to {Quote(Name(draftBase))}."
					+ " Select that grid before building.";

			yield return Validated;

			yield return HoldCubePlacer(true);

			var built = new List<Vector3I>();

			try
			{
				for(;;)
				{
					int placedThisPass = 0;

					for(int i = 0; i < draft.Count; ++i)
					{
						var d = draft[i];

						IMySlimBlock neighbour;
						Vector3I neighbourCell;
						if(CheckBuildSite(d.Cell, out neighbour, out neighbourCell) != null) continue;

						yield return PlaceCube(d.Definition, d.Cell, d.Forward, d.Up);

						built.Add(d.Cell);
						draft.RemoveAt(i);
						--i;
						++placedThisPass;
					}

					if(placedThisPass == 0) break;
				}

				yield return HoldCubePlacer(false);
			}
			finally
			{	// An error above disposes the whole coroutine stack; the placer must not stay in hand.
				SwitchCubePlacer(false);
			}

			if(draft.Count == 0) ClearDraft(); else TouchDraft();

			var result = new StringBuilder();
			result.Append($"Placed {built.Count} block(s) at minimum integrity");
			if(built.Count != 0)
			{	result.Append(": ");
				for(int i = 0; i < built.Count; ++i)
				{	if(i != 0) result.Append("; ");
					result.Append(IJK(built[i]));
				}
			}
			result.Append(".\n");

			if(draft.Count == 0)
			{	result.Append("The draft is done. Weld the frames now — `integrity` lists what is unfinished.");
				yield return Success(result.ToString());
			}

			var remaining = new List<Vector3I>();
			foreach(var d in draft) remaining.Add(d.Cell);

			var nearest = NearestToEngineer(remaining);

			IMySlimBlock n;
			Vector3I nc;
			var why = CheckBuildSite(nearest, out n, out nc);

			result.Append($"{draft.Count} block(s) still in the draft. Nearest is {IJK(nearest)} — {why}\n");

			yield return Incomplete(result.ToString());
		}
	}
}
