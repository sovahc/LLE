# Character Inventory API — Getting Items and Their Amounts

## Getting the Inventory

```csharp
// IMyCharacter inherits GetInventory() from IMyEntity
var inventory = character.GetInventory() as MyInventory;
// or (returns IMyInventory)
var inventory = character.GetInventory();
```

## Pattern 1: `GetItems()` — Allocating (Obsolete but Working)

```csharp
var inventory = character.GetInventory() as MyInventory;
if (inventory?.ItemCount > 0)
{
    var items = inventory.GetItems(); // returns List<IMyInventoryItem>
    for (int i = 0; i < items.Count; i++)
    {
        var item = items[i];
        var definitionId = item.Content.GetId(); // MyDefinitionId
        var amount = item.Amount;                // MyFixedPoint
    }
}
```

## Pattern 2: `GetItems(List<MyInventoryItem>)` — Non-Allocating (Recommended)

```csharp
var inventory = character.GetInventory() as MyInventory;
if (inventory != null)
{
    List<MyInventoryItem> invItems = new List<MyInventoryItem>();
    invItems.Clear();
    inventory.GetItems(invItems); // fills the list

    for (int i = 0; i < invItems.Count; i++)
    {
        var item = invItems[i];
        var type = item.Type;      // MyItemType (TypeId + SubtypeId)
        var amount = item.Amount;  // MyFixedPoint
        var itemId = item.ItemId;  // uint, unique within inventory
    }
}
```

## Key Types

| Type | Namespace | Description |
|------|-----------|-------------|
| `MyInventory` | `Sandbox.Game` | Real inventory class |
| `IMyInventory` | `VRage.Game.ModAPI` | Mod-facing interface |
| `MyInventoryItem` | `VRage.Game.ModAPI.Ingame` | Struct with `Type`, `Amount`, `ItemId` |
| `IMyInventoryItem` | `VRage.Game.ModAPI.Ingame` | Interface with `Content`, `Amount`, `ItemId` |
| `MyItemType` | `VRage.Game.ModAPI.Ingame` | Struct with `TypeId` and `SubtypeId`, castable to `MyDefinitionId` |

## `MyInventoryItem` Properties (Struct)

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `MyItemType` | Item type (equivalent to `MyDefinitionId`) |
| `Amount` | `MyFixedPoint` | Quantity or weight |
| `ItemId` | `uint` | Unique stack ID within the inventory |

## `IMyInventoryItem` Properties (Interface)

| Property | Type | Description |
|----------|------|-------------|
| `Content` | `MyObjectBuilder_Base` | Cast to `MyObjectBuilder_PhysicalObject` |
| `Amount` | `MyFixedPoint` | Quantity or weight |
| `ItemId` | `uint` | Unique stack ID within the inventory |

## Useful Inventory Methods

| Method | Description |
|--------|-------------|
| `GetItemAmount(MyDefinitionId)` | Get amount of a specific item |
| `ContainItems(amount, definitionId)` | Check if items exist |
| `ItemCount` | Number of occupied slots |
| `FindItem(contentId)` | Find first stack by type |
| `Empty()` | Check if inventory is empty |
| `IsFull` | Check if inventory is full |

## Source

Patterns extracted from the **AiEnabled** mod (`~/Projects/SpaceEngineers_mods/2596208372/`), specifically:
- `Bots/Roles/Helpers/RepairBot.cs` — non-allocating pattern
- `Bots/Roles/Helpers/ScavengerBot.cs` — allocating pattern
- `ConfigData/HelperData.cs` — iterating with `Content.GetId()` and `Amount`
- Decompiled `VRage.Game.dll` — API signatures
