using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using WTF_IMyInventory = VRage.Game.ModAPI.IMyInventory;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
		internal CommandResult Inventory(TokenParser tp)
		{
			if(tp.End)
			{
				var inv = character.GetInventory() as IMyInventory;
				if (inv == null) return IE_NO_INVENTORY;

				StringBuilder sb = new StringBuilder();
				sb.Append($"Your inventory:\n");
				InventoryToText(inv, sb);

				return Success(sb.ToString());
			}
			else
			{	string message;

				if(!GridIsSet(out message)) return message;
				if(CurrentGridIsProjection(out message)) return message;

				Vector3I ijk;
				if(!tp.NextVector3I(out ijk)) return "Error: expected I J K";

				var block = selectedGrid.GetCubeBlock(ijk);
				if(block == null) return $"Error: no block at {IJK(ijk)}";

				var name = Name(block);
				var fat = block.FatBlock;

				if(fat == null || !fat.HasInventory)
					return $"Block {Quote(name)} does not have an inventory.";

				StringBuilder sb = new StringBuilder();
				var es = fat.InventoryCount == 1 ? "" : "es";
				sb.Append($"Current inventory{es} of {Quote(name)} (at {IJK(ijk)}):\n");

				for(int i = 0; i < fat.InventoryCount; ++i)
				{	var inv = fat.GetInventory(i);
					InventoryToText(inv, sb);
				}

				AppendGasTankInfo(block, sb);

				return Success(sb.ToString());
			}
		}

		internal CommandResult Inventories()
		{
			string message;
			if (!GridIsSet(out message)) return message;
			if (CurrentGridIsProjection(out message)) return message;

			StringBuilder sb = new StringBuilder();
			sb.Append($"# Inventories on {Quote(Name(selectedGrid))}\n");

			var inventories = (selectedGrid as MyCubeGrid).Inventories;
			if (inventories.Count == 0) return Success("No blocks with inventories on this grid.");

			foreach (MyCubeBlock block in inventories)
			{
				if (block.CubeGrid != selectedGrid) continue;

				sb.Append($"## {Quote(Name(block.SlimBlock))} at {IJK(block.Position)}\n");
				for (int i = 0; i < block.InventoryCount; ++i)
					InventoryToText(block.GetInventory(i), sb);
				AppendGasTankInfo(block.SlimBlock, sb);
			}

			return Success(sb.ToString());
		}

		private static void InventoryToText(IMyInventory inv, StringBuilder output)
		{
			output.Append($"Used {Volume((double)inv.CurrentVolume)}/{Volume((double)inv.MaxVolume)} ({Percent(inv.VolumeFillFactor)})\n");

			if(inv.ItemCount == 0)
			{	output.Append("-- No items --\n");
				return;
			}

			var richInv = inv as WTF_IMyInventory;
			foreach (var item in richInv.GetItems())
			{
				var contentId = item.Content.GetId();
				var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(contentId);

				var volume = (double)item.Amount * itemDef.Volume;

				output.Append($"* {Quote(itemDef.DisplayNameText)} → {(double)item.Amount:F2} ({Volume(volume)})\n");
				var gasContainer = item.Content as MyObjectBuilder_GasContainerObject;
				if (gasContainer != null)
				{
					var oxygenDef = itemDef as MyOxygenContainerDefinition;
					if (oxygenDef != null)
					{
						double totalGas = gasContainer.GasLevel * oxygenDef.Capacity * (double)item.Amount;
						double maxGas = oxygenDef.Capacity * (double)item.Amount;
						string gasName = oxygenDef.StoredGasId.SubtypeName;
						output.Append($"  ({Percent(gasContainer.GasLevel)} {gasName}, {Volume(totalGas)}/{Volume(maxGas)})\n");
					}
				}
			}
		}

		private static void AppendGasTankInfo(IMySlimBlock slim, StringBuilder output)
		{
			var tank = slim.FatBlock as IMyGasTank;
			if (tank == null) return;

			var tankDef = slim.BlockDefinition as MyGasTankDefinition;
			if (tankDef == null) return;

			string gasName = tankDef.StoredGasId.SubtypeName;
			double current = tank.FilledRatio * tank.Capacity;
			double max = tank.Capacity;

			output.Append($"  ({Percent((float)tank.FilledRatio)} {gasName}, {Volume(current)}/{Volume(max)})\n");
		}

		internal IEnumerator Get(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			double count; Vector3I ijk;

			if(!tp.NextDouble(out count)) yield return "Error: expected count";

			var item = tp.NextString();

			if(!tp.Match("from")) yield return "Error: expected 'from'";

			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			var fat = block.FatBlock;
			var fromName = Name(block);

			if(fat == null || !fat.HasInventory)
				yield return $"Block {Quote(fromName)} does not have an inventory.";

			if(!IsAtInteractionPoint(block, InteractionKind.Inventory, out message))
				yield return message;

			Vector3D bp;
			if(!Collisions.GetNearestDetectorCenterByPrefix(block, GetEngineerCenter(), "conveyor_", out bp))
				block.ComputeWorldCenter(out bp);

			SetPause(2.0);
			while(IsPaused())
			{	CharacterRotateTo(bp);
				yield return null;
			}

			// recheck
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				fromList.Add(fat.GetInventory(ii));

			toList.Add(character.GetInventory());

			List<string> items = new List<string>() { item };

			StringBuilder sb = new StringBuilder();
			sb.Append($"Transferring from {fromName} into your inventory\n");

			var full = InventoryTransfer(fromList, toList, items, (MyFixedPoint)count, sb);

			yield return full ? Success(sb.ToString()) : Incomplete(sb.ToString());
		}

		internal IEnumerator Put(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			string item = null;
			double count = 0;
			bool allComponents = false;
			bool allOfItem = false;

			if(tp.Match("all"))
			{	if(tp.Match("components"))
					allComponents = true;
				else
				{	item = tp.NextString();
					allOfItem = true;
				}
			}
			else
			{	if(!tp.NextDouble(out count)) yield return "Error: expected count";
				item = tp.NextString();
			}

			if(!tp.Match("into")) yield return "Error: expected 'into'";

			Vector3I ijk;

			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			var inv = character.GetInventory() as IMyInventory;
			if (inv == null) yield return IE_NO_INVENTORY;

			var fat = block.FatBlock;
			var toName = Name(block);

			if(fat == null || !fat.HasInventory)
				yield return $"Block {Quote(toName)} does not have an inventory.";

			if(!IsAtInteractionPoint(block, InteractionKind.Inventory, out message))
				yield return message;

			Vector3D bp;
			if(!Collisions.GetNearestDetectorCenterByPrefix(block, GetEngineerCenter(), "conveyor_", out bp))
				block.ComputeWorldCenter(out bp);

			SetPause(2.0);
			while(IsPaused())
			{	CharacterRotateTo(bp);
				yield return null;
			}

			// recheck
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			fromList.Add(character.GetInventory());

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				toList.Add(fat.GetInventory(ii));

			StringBuilder sb = new StringBuilder();
			sb.Append($"Transferring from your inventory into {toName}\n");

			if(allComponents)
			{
				var full = InventoryTransfer(fromList, toList, ALL_COMPONENTS, MyFixedPoint.MaxValue, sb);

				yield return full ? Success(sb.ToString()) : Incomplete(sb.ToString());
			}
			else
			{	List<string> items = new List<string>() { item };
				var amount = allOfItem ? MyFixedPoint.MaxValue : (MyFixedPoint)count;
				var full2 = InventoryTransfer(fromList, toList, items, amount, sb);
				yield return full2 ? Success(sb.ToString()) : Incomplete(sb.ToString());
			}
		}

		internal IEnumerator Transfer(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			double count = 0; Vector3I ijkFrom, ijkTo;
			bool allItems = false;
			string item = null;

			if(tp.Match("all") && tp.Match("items"))
			{	allItems = true;
			}
			else
			{	if(!tp.NextDouble(out count)) yield return "Error: expected count";
				item = tp.NextString();
			}

			if(!tp.Match("from")) yield return "Error: expected 'from'";

			if(!tp.NextVector3I(out ijkFrom)) yield return "Error: expected I J K";

			if(!tp.Match("to")) yield return "Error: expected 'to'";

			if(!tp.NextVector3I(out ijkTo)) yield return "Error: expected I J K";

			var blockFrom = selectedGrid.GetCubeBlock(ijkFrom);
			if(blockFrom == null) yield return $"Error: no block at {IJK(ijkFrom)}";

			var blockTo = selectedGrid.GetCubeBlock(ijkTo);
			if(blockTo == null) yield return  $"Error: no block at {IJK(ijkTo)}";

			var fatFrom = blockFrom.FatBlock;
			var fromName = Name(blockFrom);

			if(fatFrom == null || !fatFrom.HasInventory)
				yield return $"Block {Quote(fromName)} does not have an inventory.";

			var fatTo = blockTo.FatBlock;
			var toName = Name(blockTo);

			if(fatTo == null || !fatTo.HasInventory)
				yield return $"Block {Quote(toName)} does not have an inventory.";

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fatFrom.InventoryCount; ++ii)
				fromList.Add(fatFrom.GetInventory(ii));

			for (int ii = 0; ii < fatTo.InventoryCount; ++ii)
				toList.Add(fatTo.GetInventory(ii));

			StringBuilder sb = new StringBuilder();
			sb.Append($"Transferring from {fromName} into {toName}\n");

			if(allItems)
			{	var full = InventoryTransfer(fromList, toList, new List<string>(), MyFixedPoint.MaxValue, sb, requireConveyor: true);
				yield return full ? Success(sb.ToString()) : Incomplete(sb.ToString());
			}
			else
			{	List<string> items = new List<string>() { item };
				var full = InventoryTransfer(fromList, toList, items, (MyFixedPoint)count, sb, requireConveyor: true);
				yield return full ? Success(sb.ToString()) : Incomplete(sb.ToString());
			}
		}

		private static bool Include(MyPhysicalItemDefinition def, List<string> itemNames)
		{
			if(def == null) return false; // shouldn't happen
			if(itemNames.Count == 0) return true; // empty filter = match every item

			foreach(var name in itemNames)
			{	if(def.DisplayNameText == name) return true;
				if(def.Id.SubtypeName == name) return true;
			}
			return false;
		}

		internal bool InventoryTransfer(List<IMyInventory> fromList, List<WTF_IMyInventory> toList,
			List<string> itemNames, MyFixedPoint amount, StringBuilder result,
			bool requireConveyor = false)
		{	
			if(requireConveyor)
			{
				bool hasConnection = false;

				foreach(var from in fromList)
					foreach(var to in toList)
						if(from.IsConnectedTo(to))
						{	hasConnection = true;
							break;					
						}

				if(!hasConnection)
				{	result.Append("Error: Inventories are not connected via the conveyor system, direct transfer is not possible.");
					return false;
				}
			}

			List<MyInventoryItem> items = new List<MyInventoryItem>();

			bool somethingTransferred = false;
			bool noSpaceWarning = false;

			foreach(IMyInventory fromInv in fromList)
			{	
				items.Clear();
				fromInv.GetItems(items);

				for(int i = items.Count - 1; i >= 0; --i)
				{	var item = items[i];
					var def = (MyDefinitionId)item.Type;
					var itemDef = MyDefinitionManager.Static.GetDefinition(def) as MyPhysicalItemDefinition;

					if(!Include(itemDef, itemNames)) continue;

					var transfer = amount;
					if(transfer > item.Amount) transfer = item.Amount;
					
					MyFixedPoint transferred = InventoryTransfer(fromInv, i, toList, transfer, requireConveyor);
					if(transferred > 0)
					{
						somethingTransferred = true;
						result.Append($"Transferred {transferred} {Quote(itemDef.DisplayNameText)}\n");
						if(transferred < transfer) noSpaceWarning = true;
					}
				}
			}

			if(noSpaceWarning)
			{	result.Append($"Warning: Not all items were transferred due to insufficient inventory space.\n");
			}

			if(somethingTransferred)
			{
				PlaySound("PlayDropItem");
			}
			else
			{	result.Append($"No items transferred!\n");
			}

			return somethingTransferred && !noSpaceWarning;
		}

		internal static MyFixedPoint InventoryTransfer(IMyInventory from, int fromIndex, List<WTF_IMyInventory> toList, MyFixedPoint amount,
			bool checkConnection = false)
		{
			List<MyInventoryItem> items = new List<MyInventoryItem>();
			from.GetItems(items);

			var item = items[fromIndex];
			MyFixedPoint transferred = 0;

			foreach (var to in toList)
			{
				if (transferred >= amount) break;

				var toInv = to as MyInventory;
				if (toInv == null) continue;

				MyFixedPoint fits = toInv.ComputeAmountThatFits(item.Type);
				if (fits <= 0) continue;

				MyFixedPoint toTransfer = MyFixedPoint.Min(amount - transferred, fits);

				// checkConnection makes the game require a conveyor path between the two
				// inventories; a character has no conveyor endpoint, so it must stay off there.
				if(!((WTF_IMyInventory)from).TransferItemTo(to, fromIndex, null, true, toTransfer, checkConnection))
					continue;

				transferred += toTransfer;
			}

			return transferred;
		}

		internal static void InventoryDelta(IMyInventory inv, Dictionary<string, double> current, int sign)
		{
			List<MyInventoryItem> items = new List<MyInventoryItem>();
			inv.GetItems(items);
			foreach (var item in items)
			{
				var def = (MyDefinitionId)item.Type;
				var itemDef = MyDefinitionManager.Static.GetDefinition(def) as MyPhysicalItemDefinition;
				if(itemDef == null) continue;

				var name = itemDef.DisplayNameText;
				double amount = (double)item.Amount;

				if (current.ContainsKey(name))
					current[name] += sign * amount;
				else
					current[name] = sign * amount;
			}
		}

		internal static void InventoryDiff(Dictionary<string, double> delta, StringBuilder result)
		{
			bool added = false;

			foreach (var kv in delta)
			{
				if(Math.Abs(kv.Value) < 1e-3) continue;

				string operation = kv.Value < 0 ? "Added" : "Removed";
				result.Append($"{operation} {Math.Abs(kv.Value)} {Quote(kv.Key)}\n");

				added = true;
			}

			if(!added) result.Append("none\n");
		}
	}
}
