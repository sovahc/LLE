# Space Engineers Serialization for Binary Sockets

## 1. ProtoBuf via Mod API (Recommended)

**Available through `MyAPIGateway.Utilities`:**

```csharp
// Serialize object → byte[]
byte[] data = MyAPIGateway.Utilities.SerializeToBinary(obj);

// Deserialize byte[] → object
T obj = MyAPIGateway.Utilities.SerializeFromBinary<T>(data);
```

Built on top of `ProtoBuf.Net.dll` (located in `Bin64/`). This is the **only binary serialization method available to mods**.

### Defining serializable types:

```csharp
using ProtoBuf;

[ProtoContract]
public class MyPacket
{
    [ProtoMember(1)]
    public string Name;

    [ProtoMember(2)]
    public int Value;

    [ProtoMember(3)]
    public Vector3 Position; // VRageMath types are supported out of the box
}
```

### Polymorphism (inheritance):

```csharp
[ProtoContract]
[ProtoInclude(10, typeof(ChatCommand))]
[ProtoInclude(11, typeof(SyncData))]
public abstract class Packet { }

[ProtoContract]
public class ChatCommand : Packet
{
    [ProtoMember(1)]
    public string Message;
}
```

---

## 2. Manual Serialization via BinaryWriter / BinaryReader

`BinaryWriter`, `BinaryReader`, and `MemoryStream` are in the script whitelist. You can write manually:

```csharp
using (var ms = new MemoryStream())
using (var bw = new BinaryWriter(ms))
{
    bw.Write(myInt);
    bw.Write(myFloat);
    bw.Write(Encoding.UTF8.GetBytes(myString));
    byte[] data = ms.ToArray();
}
```

---

## 3. BitStream (NOT available to mods)

The class `VRage.Library.Collections.BitStream` is internal, used by the engine for multiplayer networking. **Not in the whitelist**, not accessible from mods.

---

## Summary

To send data over a socket, use:
- **Write:** `MyAPIGateway.Utilities.SerializeToBinary(obj)` → get `byte[]` to send.
- **Read:** `SerializeFromBinary<T>(data)` on the receiving side.
