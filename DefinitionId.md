# MyDefinitionId Serialization in Space Engineers

## Overview
There are two primary ways to serialize `MyDefinitionId` (TypeId + SubtypeId) in Space Engineers mods, particularly observed in the "AI Enabled" mod.

## 1. Native Approach: `SerializableDefinitionId`
This is the standard API struct provided by the game (`VRage.ObjectBuilders.SerializableDefinitionId`).

### Structure
*   **TypeId**: `ushort` (2 bytes). Represents the `MyObjectBuilderType` as a runtime ID.
*   **SubtypeId**: `MyStringHash` (4 bytes). A 32-bit integer hash of the subtype string.

### Usage
*   **Implicit Conversion**: `MyDefinitionId` can be directly assigned to `SerializableDefinitionId` and vice-versa.
*   **Protobuf**: Marked with `[ProtoContract]`, serializing the two integers directly.

### Pros & Cons
*   **Pros**: Extremely compact (6 bytes total). Fast serialization.
*   **Cons**: Relies on `MyStringHash` which is prone to collisions (see below).

## 2. Custom Approach: String-based Wrapper
Some mods (like AI Enabled's `SerialId`) implement a custom wrapper to store IDs as strings.

### Structure
*   **TypeId**: `string`
*   **SubtypeId**: `string`

### Usage
*   **Manual Parsing**: Requires `MyObjectBuilderType.TryParse` to reconstruct the `MyDefinitionId` from strings.
*   **Protobuf**: Serializes the strings directly.

### Pros & Cons
*   **Pros**: Human-readable (useful for XML/JSON). No hash collision risks.
*   **Cons**: Verbose (variable length). Slower parsing.

---

## Deep Dive: `MyStringHash` and Collisions

### The Problem
`MyStringHash` uses a 32-bit integer to represent strings. With a large number of unique strings (mods, blocks, items), hash collisions are mathematically inevitable (Birthday Paradox).

### The Solution: Fail-Fast Strategy
The game developers chose to **crash immediately** if a collision occurs, rather than risk silent data corruption.

### Implementation Details
1.  **Global Dictionaries**: The game maintains two static dictionaries:
    *   `m_stringToHash`: Maps `string` -> `MyStringHash`.
    *   `m_hashToString`: Maps `MyStringHash` -> `string`.
2.  **Registration**: When a string is converted to a hash via `GetOrCompute`:
    *   It calculates the 32-bit hash.
    *   It attempts to `Add` the hash to `m_hashToString`.
    *   **Crash Condition**: If the hash already exists in the dictionary (associated with a different string), `Dictionary.Add` throws an exception, crashing the game.

### Implications for Modders
*   **Startup Crashes**: If your mod introduces a block/item name that collides with an existing one, the game will fail to start.
*   **Serialization Safety**: As long as the game starts, the hash-to-string mapping is valid. Deserializing a hash back to a string is safe *provided* the original string was registered before the save was loaded.
