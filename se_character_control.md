# Space Engineers — Character/Bot Movement Control

## Primary Movement Method: `MoveAndRotate`

```csharp
// movement  — local direction vector (Forward/Backward/Up/Down/Left/Right)
// rotation  — Vector2(x=pitch, y=yaw)
// roll      — optional roll angle
Character.MoveAndRotate(movement, rotation, roll);
```

Local direction axes:
- `Vector3.Forward`, `Vector3.Backward` — forward/backward
- `Vector3.Up`, `Vector3.Down`         — up/down (jetpack)
- `Vector3.Left`, `Vector3.Right`      — strafe left/right

## Jetpack Component — `MyCharacterJetpackComponent`

```csharp
var jetComp = Character.Components.Get<MyCharacterJetpackComponent>();

jetComp.TurnOnJetpack(true);   // enable
jetComp.TurnOnJetpack(false);  // disable
bool on = jetComp.TurnedOn;    // status (read-only)
bool running = jetComp.Running;// active and powered (read-only)

// Thrust vector (read-only, final applied thrust)
Vector3 thrust = jetComp.FinalThrust;
```

## Direct Physics Control

```csharp
Character.Physics.LinearVelocity   = newVel;          // set linear velocity directly
Character.Physics.SetSpeeds(linear, angular);         // set both velocities at once
```

## Speed Clamping (from AiEnabled)

```csharp
var vel = Character.Physics.LinearVelocity;
if (vel.LengthSquared() > max * max)
    Character.Physics.LinearVelocity = Vector3.Normalize(vel) * max;
```

## Other Methods & Properties

| Member | Description |
|---|---|
| `Character.Jump()` | Trigger jump animation + physics impulse |
| `Character.SwitchWalk()` | Toggle walk / fly mode |
| `Character.MovementFlags` | Flags: Jump, Sprint, FlyUp, FlyDown, Crouch, Walk |

## Key Insight from AiEnabled

AiEnabled does **not** set the thrust vector directly. It calls `MoveAndRotate(movement, rotation, roll)`, and the game internally converts this via the jetpack component into `ThrustComp.ControlThrust += moveDirection * ForceMagnitude`.

To control thrust direction directly, you need access to the internal `MyJetpackThrustComponent.ControlThrust` (via reflection or if the component exposes it).

## Source Reference

- AiEnabled mod (`/SpaceEngineers_mods/2596208372/`)
  - `AiEnabled/Bots/BotBase.cs` — `MoveToPoint()`, `SetVelocity()`, `AdjustMovementForFlight()`
  - `AiEnabled/Support/Controls.cs` — factory terminal controls
