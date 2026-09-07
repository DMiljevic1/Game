# CLAUDE.md — Unity Lab

Guidance for Claude Code working in this repository.

## Project facts

| | |
|---|---|
| Engine | **Unity 6000.5.9f1** (Unity 6) |
| Render pipeline | **URP 17.5.0** — shader name is `Universal Render Pipeline/Lit`, **not** `Standard` |
| Input | `activeInputHandler: 2` (**Both**). Existing gameplay scripts use the **legacy `Input` API** (`Input.GetAxis`, `Input.GetKeyDown`). Match that unless asked to migrate. |
| Language | C# 9, no custom namespaces, no assembly definitions |
| Scripts | New game systems go in `Assets/Scripts/`. The three original prototype scripts (`PlayerMovement`, `MouseLook`, `DoorInteraction`) are still flat in `Assets/` — move them with `AssetDatabase.MoveAsset` if tidying, never by hand. |
| Main scene | `Assets/Scenes/SampleScene.unity` |
| Platform | Windows. Bash tool is Git Bash; a PowerShell tool is also available. |

URP asset variants live in `Assets/Settings/` (`PC_RPAsset`, `Mobile_RPAsset`). Don't edit them without being asked.

## Working through the Unity MCP

The `unity-mcp` tools drive the **live Editor**. That is the primary way to change the scene.

- **Never hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` YAML.** GUIDs and file IDs are easy to corrupt and Unity will silently drop the object. Use `Unity_RunCommand` instead.
- `Unity_RunCommand` code must be `internal class CommandScript : IRunCommand`. Public accessibility or a different class name fails to compile.
- Use the `result` object so changes land in the undo stack:
  `result.RegisterObjectCreation(go)` after creating, `result.RegisterObjectModification(o)` **before** mutating, `result.DestroyObject(go)` instead of `DestroyImmediate`.
- **Inspect before you edit.** Dump the hierarchy (names, positions, rotations, lossy scales) first — this scene uses rotated wall cubes where `localScale.x` is the wall's *length*, not its thickness, so guessing axes produces walls facing the wrong way.
- **Finish the job in the Editor.** Don't hand back instructions like "now drag the player into this field" — wire references from the command script.
- **Save the scene** when a change is complete: `EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene())`. Until then edits exist only in memory ("scene is dirty") and are lost if Unity closes.
- Verify visually with `Unity_Camera_Capture` (frame it first via `SceneView.lastActiveSceneView.LookAt`). Use `Unity_SceneView_CaptureMultiAngleSceneView` only when multiple angles genuinely matter — it's expensive.
- Check `Unity_GetConsoleLogs` after anything that could throw at runtime.
- Any throwaway editor script written to disk goes in `Assets/Editor/TemporaryGeneratedScripts/` and is deleted in the same session. Prefer `Unity_RunCommand`, which leaves nothing behind.

## Scene conventions (SampleScene)

- Roots: `Directional Light`, `Global Volume`, `Player` (CharacterController + `PlayerMovement`, child `Main Camera` + `MouseLook`), `Systems`, `Generator`, `FuelCans`, `PlayerRespawn`, `Ground`, `Boundary`, `House`, `Forest`, `Props`.
- **The environment is generated, not hand-placed.** `Assets/Editor/PrototypeEnvironmentBuilder.cs`
  (menu **Lab ▸ Environment ▸ Build Prototype Environment**) rebuilds `Ground`, `Boundary`, `House`,
  `Forest` and `Props` from scratch out of primitives. Change the level by editing that script and
  re-running it, not by dragging cubes — a re-run destroys every root outside its `Keep` set
  (`Player`, `Systems`, `Directional Light`, `Global Volume`, `Generator`, `PlayerRespawn`, `FuelCans`,
  `Flashlight`, `SellStation`, `Valuables`),
  so anything hand-placed elsewhere is lost. It is deterministic (`Random.InitState`), so the same
  seed gives the same forest.
- `House` is one abandoned building centred on the origin, 14 × 11, split by a south→north hallway:
  **Entrance** (SE) → **Hallway** → **Living room** (SW), **Bedroom** (NW), **Kitchen** (NE).
  Children group under `Shell`, `Walls`, `Doors`, `Detail`, `Porch`, `Lights`, `Furniture`.
- **Geometry reference points:** floor tops sit at **y = 0.05**; walls are 3.0 tall, **0.15 thick**
  outside / 0.12 inside; the flat ceiling is at y = 3.08 with a pitched roof above it; door openings
  are **1.2 wide × 2.2 high**, window openings run **y 1.05 → 2.15**.
- Walls are built by `WallRun` from two endpoints plus a list of `Opening`s (doorway = bottom 0,
  window = bottom above the floor). They are **axis-aligned and unrotated** — for a wall along x the
  box's x is its length, for a wall along z its z is. Add openings there rather than carving cubes.
- `Ground` is a scaled cube (not a Plane) with its top at **y = 0.04** — 1 cm under the floors, so overlapping faces don't z-fight.
- `Boundary` holds four **collider-only** `GameObject`s (no `MeshRenderer`) at the grass edges. They are derived from `Ground`'s renderer bounds — if the ground is resized, rebuild them from those bounds rather than hardcoding numbers.
- Only trunks and large rocks carry colliders; canopies, bushes and small debris are render-only, so
  the forest stays cheap to walk through.

### Door pattern — follow it exactly

A doorway is the wall's jamb/lintel pieces (produced by `WallRun`) plus a hinge.

- The hinge is an **empty `GameObject` at the opening's edge** carrying `DoorInteraction`; the door cube is its **child** at `localPosition = (0.6, 0, 0)` with `localScale = (1.1, 2.2, 0.15)`.
- `DoorInteraction` must be on the hinge, never on the door mesh — it rotates its own transform.
- `openAngle = 90` rotates the hinge's local +x toward its local −z. Work out which way that swings *before* placing the hinge so the door doesn't sweep through furniture or a wall.
- `DoorInteraction` needs no player reference — `PlayerInteractor` does the targeting.

## The game (co-op survival horror)

Design doc: `C:\Users\dujem\OneDrive\Documents\GameDux.txt`. Read it before proposing gameplay work.
Short version: 1–4 player co-op, ~8 wholly distinct locations. Explore by day, survive by night;
a generator keeps monsters out of the house while it has fuel. Progress comes from finding
journal fragments and a map that reveal where the next house is. Survival over combat, no bosses.

**Co-op is the target, single-player is the current stage.** Write every system netcode-shaped:
one authority object owns each piece of shared state, no per-player statics, no game logic living
in UI. `TimeOfDay.HasAuthority` is the pattern — a seam that becomes `IsServer` when Netcode lands.

### TimeOfDay — the level clock

`Assets/Scripts/TimeOfDay.cs` on the `Systems` object is the **single authoritative clock**.
Every time-dependent system (generator drain, monster spawning, ambience) reads it and never
keeps its own timer.

- Phases: Day 8:00 → Dusk 2:00 → Night 5:00 → Dawn 1:00 (a 16-minute cycle), scaled by `timeScale`.
- Subscribe to `OnPhaseChanged` / `OnDayStarted`; query `Phase`, `IsNight`, `IsDark`, `TimeUntilPhaseEnd`.
- Sunrise (06:00) and sunset (18:00) fall in the **middle** of dawn and dusk, so those phases are
  the light actually changing. Don't remap hours without preserving that.
- Only the authority advances time. `SetCycleTime` exists for client mirroring and editor preview.

### Generator — the night's pressure

`Assets/Scripts/Generator.cs` on the `Generator` object beside the country house.

- Ask **`Generator.IsPointProtected(pos)`** — never test a radius by hand. Monsters must respect it.
- Fuel drain is multiplied by `TimeOfDay.timeScale`, so a tank always lasts the same fraction of a
  night no matter what speed we test at. Anything else that consumes over time must do the same.
- Tuning as of now: 100 fuel, 0.25/s = **400s of runtime vs a 300s night** — night 1 is survivable
  on a full tank, night 2 is not without refuelling. That is the intended teaching curve (doc §16).
- Subscribe to `OnRanOutOfFuel` / `OnLowFuel` / `OnStarted` / `OnStopped` rather than polling.
- `poweredLights` are switched with the running state: protection has to be *visible*.
- Note: the static registry is filled in `OnEnable`, which does not run in edit mode. Editor-time
  calls to `IsPointProtected` return false — verify radii geometrically in tooling, not via that call.

### Interaction — one router, never a second key handler

`PlayerInteractor` on the Player picks **one** target per frame, lists what can be done to it, and
dispatches the key. **Never** add proximity checks or `Input.GetKeyDown` to a new object — implement
`IInteractable` instead, or two things will fire at once.

**Targeting is forgiving, not pixel-perfect.** A dead-centre ray still wins outright, so deliberate
aim is always authoritative. Otherwise everything within `range` is gathered with one
`OverlapSphereNonAlloc`, scored on `angle/aimTolerance * aimWeight + distance/range * distanceWeight`
(lowest wins), sorted, and the first candidate that is **both visible and willing** is taken.

- The aim point is the spot on the collider nearest the view ray, so tolerance scales with size:
  a door is forgiving across its whole face, a dropped torch still gets a real area.
- A candidate that returns no options must not block the ones behind it — that is why the search
  walks a sorted list instead of committing to the single best score. An empty-handed player at the
  sell counter still gets the door prompt.
- Line of sight is rechecked per candidate (`HasLineOfSight`), which is the only thing stopping
  interaction through walls. The ray stops 2 cm short so an object cannot occlude itself.
- `aimTolerance` under 90° is what excludes things beside and behind the player. Don't raise it past
  that; there is a `[Range(1, 85)]` on the field for this reason.
- Measured with the defaults (range 3.5, tolerance 32°): doors ~±50° of yaw, the sell counter ~±56°,
  a torch on the floor a 42°-tall by 114°-wide window. Near a door, the torch needs a deliberate look
  down — the door legitimately wins at head height.

**Key map (the interactable declares its own key, so nothing can silently steal one):**

| Key | Action | Declared by |
|---|---|---|
| **E** | open / close doors, **and** sell at the counter | `DoorInteraction.useKey`, `SellStation.sellKey` |
| **F** | pick up an item, and pour fuel in | `Carryable.pickUpKey`, `Generator.refuelKey` |
| **Q** | drop what you are holding | `PlayerInteractor.dropKey` |
| **T** | start / stop the generator | `Generator.powerKey` |
| **X** | switch the torch on / off | `Flashlight.toggleKey` |

- `GetOptions` fills a list, so one object can offer several actions at once — the generator offers
  refuel (F) and start (T) together, so you never have to put the can down to switch it on.
- **Every interaction makes a noise**, emitted once by the router (`interactNoiseRadius`, 12m) so a
  new interactable is audible to the blind monster without doing anything. Keys routed to a *held*
  item are silent on purpose.
- Add nothing to the list to refuse interaction entirely.
- Ask what the player is holding with `interactor.GetCarried<T>()`; that is how the generator knows
  whether to offer refuel at all.
- Carryables (`Carryable`, `FuelCan`, `Flashlight`) disable their colliders while held so they neither
  shove the player nor block the interaction ray.
- **The interactor is also what makes a held item heavy.** Picking up applies the item's `CarryLoad`
  to `PlayerMovement`; every way it leaves the hands clears it. See *Weight* under Money and selling.
- **Every way an item leaves the hands goes through `DropCarriedAt`** — the Q drop passes the spot in
  front of the player, death passes `PlayerVitals.LastDeathPosition`. One path, so a death drop and a
  Q drop can never drift apart. `GroundAt` puts it on whatever is below, lifted by `dropClearance`
  (5 cm) so nothing sinks into the floor, and ignores the player and anything with a
  `CharacterController` — a monster standing over your corpse must not leave the loot mid-air.
- **Dying drops what you were carrying where you fell.** `PlayerInteractor` subscribes to
  `PlayerVitals.OnDied`; the interactor owns the `carried` reference, so the drop belongs there and
  `PlayerVitals` still knows nothing about carrying. The item is never destroyed, never teleported to
  the counter and never auto-sold — it becomes an ordinary world pickup, keeping its script, value
  and prompt, so recovering your own loot is a trip back out. Dying empty-handed does nothing.
  `PlayerVitals` records `LastDeathPosition` *before* `Respawn` moves the player, so a handler never
  has to rely on running before the teleport. Co-op: the item goes back to the world rather than to
  another player, so there is no per-player ownership to replicate — this becomes a server-side call
  and the item's transform is all a client needs.
- **A held item gets keys too.** Each frame the router also asks the carried item for its options and
  routes those — you switch a torch on without looking at it. Declare them from `GetOptions` while
  `IsHeld`, exactly as `Flashlight` does; never add an `Input` call. If the held item and the aimed
  target share a key the **target wins**, so one press can never fire two actions.
- `CarrySocket` is deliberately cocked (0, 340, 8) so a held prop sits at a jaunty angle. Anything
  directional — a torch beam, later a camera — must cancel that out via its own `heldEuler`, or it
  will point 20° off the crosshair.
- Anything interactable needs a collider, and colliders must be enabled to be looked at — doors are
  deliberately unhittable mid-swing for this reason.

### Economy as of now

Generator tank 100. A full night costs **75** fuel. A can holds **40**. So one can buys roughly half
a night, and a full tank plus one can does not quite cover two nights — refuelling is a recurring
trip, not a one-off errand. Retune these together, never one alone.

### Monsters

`MonsterSpawner` on `Systems` spawns a wave at **dusk** and clears it at **dawn** — "they don't come
during the day" is true in the simulation, not just in the journal. `Assets/Prefabs/Monster.prefab`.

**The monster is blind.** It has no vision and never touches the player's transform — the only way
anything reaches it is `Noise` (see below). `Monster` implements `INoiseListener`; there is no
`target` field, and `SetTarget` is a deliberate no-op kept so the spawner needn't know what it spawned.
**Never** add a raycast-to-player, a distance-to-player check, or a light/flashlight test to it —
that is the whole design, not an optimisation. It hurts what it *touches*, found by
`OverlapSphereNonAlloc`, so even the attack never asks where the player is.

**State machine** (`MonsterState`, one per frame in `Update`):

| State | Does | Leaves when |
|---|---|---|
| `Patrol` | Walks `patrolPoints` in order at `patrolSpeed`, waiting `patrolWaitTime` at each | any noise is heard |
| `Alerted` | Walks to the (blurred) noise position at `alertSpeed` | arrives → `Investigate`; louder/repeated noise → `Chase` |
| `Investigate` | Stands and turns on the spot for `investigateDuration` | timer out → `Patrol` (agitation reset); new noise → `Alerted`/`Chase` |
| `Chase` | Same as `Alerted` but `chaseSpeed`, always re-targeting the **newest** noise | `agitation` decays below `chaseThreshold` → `Alerted` |

- **`agitation`** is the aggression dial: each heard noise adds `agitationPerSound * (0.5 + clarity)`,
  it decays at `agitationDecayPerSecond`, and crossing `chaseThreshold` is what turns walking into
  running. Sounds close together stack; silence calms it down.
- `patrolPoints` empty → it falls back to random wandering in `roamCenter`/`roamRadius`, so a
  monster spawned with no route still behaves. `MonsterSpawner.patrolRoute` (the `PatrolRoute` root)
  hands its children to every spawned monster, each starting at a different index.
- Monsters must **never** decide safety by measuring distance themselves; they call
  `Generator.GetProtector` / `IsPointProtected`. `KeepOutOfSafeZone` runs on every heard position
  *and* every destination, so a noise made inside the light draws it to the boundary and no further.
  `Monster.Move` also re-checks after every move and pushes back out, so no collision slide or
  steering bug can ever put one inside the light.
- Steering is direct with a `CharacterController` (flat level, slides off walls for free).
  Switch to `NavMeshAgent` when a level has real geometry — `com.unity.ai.navigation` is installed.
- Balance now: chase 3.6 vs player walk 5 / sprint 9 — always outrunnable, so death is a mistake,
  not a dice roll. 25 damage every 1.2s = 4 hits, ~4.8s of standing still.
- `showDebug` (on by default) draws the hearing radius, the destination in the state's colour, the
  last heard position and a state/agitation label in the Scene view. `MonsterDebugHud` on `Systems`
  is the in-game readout. Both are pure observers — turning either off changes no behaviour.
- `PlayerVitals` regenerates **only inside protection**, which makes the house the only place to
  recover without saying so.
- **Doors:** a hunting monster that meets a shut door leans on it for `doorForceTime` (1.4s) and
  then it swings open. It cannot reach a door while the generator runs — the whole house sits inside
  the radius — so "they can open doors" is really "once the light dies". `DoorInteraction.canBeForced`
  can be cleared for a door that should hold.
- **Known limit:** steering is direct, so monsters navigate open ground and doorways but snag on
  interior corners. Bake a NavMesh and swap to `NavMeshAgent` before relying on indoor pursuit.
- **Known limit:** sound is not occluded. A noise through a wall carries as far as one in the open.

### Sound — how anything gets noticed

`Assets/Scripts/NoiseEvent.cs` holds the whole system: a `NoiseEvent` struct (position, radius,
source, time), an `INoiseListener` interface, and the static `Noise` bus.

- **Making a sound is always one line:** `Noise.Emit(position, radius, gameObject)`. `radius` *is*
  the loudness — how far it carries in metres. Nothing polls; listeners register in `OnEnable`.
- A listener hears it when `distance <= radius * hearingSensitivity` **and** `distance <=
  hearingRadius`. The position it is handed is blurred by `positionError * (1 - clarity)`, so a
  faint noise is a direction and a close one is a fix. A monster ignores its own `source`.
- **Who emits today:** `NoiseEmitter` on the Player (footsteps from `CharacterController.velocity`,
  walk 8m / sprint 20m / landing 14m — standing still emits nothing at all), `PlayerInteractor`
  (12m, once for *every* interaction, so a new interactable is audible the day it is written), and
  `DoorInteraction.SetOpen` (14m, the code path a monster forcing a door uses; the player's own
  door noise comes from the interactor, so opening one by hand never sounds twice).
- Keys routed to a **held** item are deliberately silent — flicking the torch on must not give you
  away, and the flashlight has no effect on a blind monster in any other way either.
- `Noise.OnNoiseEmitted` fires for every noise whether heard or not. Debug overlays only.

### Damage feedback

`DamageFeedback` on the Player and `CameraShake` on `Main Camera` are what make a hit *readable*.
Both are **pure observers**: `DamageFeedback` subscribes to `PlayerVitals.OnDamaged` and never
touches health, so deleting it costs the feel of the hit and never the damage. There is still
exactly one health system — `PlayerVitals` — and one readout, `Hud.Row(3)`.

- **No coroutines.** The flash is a single float ticked down in `Update`, so a burst of hits can
  only ever *retrigger* the effect: the new flash starts at the brighter of the two and the timer
  restarts. Nothing stacks, nothing is left running. Do the same for anything similar.
- The flash is a **vignette**, not a wash — red at the edges, ~7% opacity dead centre at its
  brightest — drawn in `OnGUI` (this project has no Canvas). The texture is built once and
  rebuilt only when `flashEdgeBias` changes.
- `CameraShake` offsets the camera's **local position** and rolls it around its own **forward
  axis**. Roll cannot change `forward`, so the interaction ray, the flashlight beam and player
  movement are all untouched — never make it yaw or pitch instead. MouseLook rewrites
  `localRotation` every `Update`, so the roll is applied in `LateUpdate` and last frame's roll is
  removed first; it can never accumulate, even with MouseLook off.
- `Shake()` is public and generic — a future fall, explosion or slammed door should call it
  rather than grow a second copy of the maths.
- **The hit sound is deliberately unassigned.** `hitSound` is an empty `AudioClip` slot on the
  Player's `AudioSource` (2D, `playOnAwake` off); there is no placeholder. Drop a clip in and it
  plays, pitch-varied so repeated hits don't machine-gun.
- Effect strength scales with `PlayerVitals.LastDamageAmount` against `damageForFullEffect` (25,
  the monster's hit), floored at `minimumIntensity` — a small scratch still registers.

### Money and selling

`Wallet` on `Systems` is the **single authority on money** — the same shape as `TimeOfDay`, with a
`HasAuthority` seam and a static `Instance`. Money is shared by the team, not per-player, so there is
exactly one Wallet in the level. Everything that pays out goes through `Wallet.Add`; `SetMoney` exists
only for client mirroring and editor preview.

- `Valuable` (a `Carryable`) is loot: **a price and a size, and nothing else**. A new item is a new
  object and two numbers, never a new script. It shows both on the pickup prompt.
- `SellStation` on the `SellStation` root is an ordinary `IInteractable`. Holding a `Valuable` offers
  **E**; holding nothing offers no prompt at all — the same rule as the generator only offering refuel
  when you actually have a can. Selling reads the price, calls `interactor.ConsumeCarried()` so the
  item leaves the hands without being dropped, destroys it, then pays the Wallet.
- **The counter needs a collider at standing eye height.** A waist-high counter alone fails: the aim
  ray sails over it into the wall and the player has to stare at the floor to sell. `Counter_Backboard`
  reaches y 2.6 for exactly this reason — don't shorten it.
- `MoneyHud` only *reads* the Wallet and its event. No price, sale or arithmetic lives in the UI;
  deleting it would cost the readout, never the money.

#### Weight — why the expensive thing is the dangerous thing

Price is only half a loot item; the other half is what carrying it costs you.

- `CarryLoad` (declared in `Carryable.cs`) is the whole vocabulary: a walk multiplier, a sprint
  multiplier and `allowSprint`. Every `Carryable` has one (`Load`), so a fuel can or a future crate
  can be heavy without touching `Valuable`. Default is `CarryLoad.None` — no penalty.
- **`PlayerMovement` is pushed the load, it never reads the item.** `SetCarryLoad` / `ClearCarryLoad`
  are the only seam; `PlayerInteractor.ApplyCarryLoad` is the only caller, and *every* route in and
  out of the hands passes through it (`Carry`, `DropCarriedAt`, `ConsumeCarried`), so a penalty can
  never outlive the item that caused it. There is still exactly one movement script.
- **A load that forbids sprinting swallows the sprint key**, rather than scaling it to nothing — so
  holding shift with the television does nothing at all instead of feeling broken.
- `ValuableSize` is the tuning dial and `Valuable.ApplySizePreset` is the one place a size becomes
  numbers (it runs in `Awake` and `OnValidate`, so the Inspector shows what the player will feel).
  Retune a category there and every item in it follows. `Custom` opts an item out and uses the
  multipliers as authored.

| Size | Walk | Sprint | Items now |
|---|---|---|---|
| `Small` | 5.0 (×1) | 9.0 (×1) | old radio $40, camera $60, old clock $75 |
| `Medium` | 4.5 (×0.9) | 8.1 (×0.9) | laptop $150 |
| `Large` | 3.1 (×0.62) | **none** | old CRT television $300 |

- The Large numbers are chosen against the monster: chase speed is **3.6**, so 3.1 means the
  television is the one thing you cannot outrun. That is the risk/reward, not a balance accident —
  retune `chaseSpeed` and the Large preset together.
- Two things fall out of this for free and should be left alone: `NoiseEmitter` reads the
  controller's *actual* speed, so carrying the television is also **quieter** than running; and
  `PlayerInteractor`'s carrying line says `(heavy)` / `(too heavy to run)`, because being slow with
  no explanation reads as a bug.
- The five items live under the `Valuables` root in `SampleScene`, built from primitives like the
  rest of the prototype. `Valuables` is in the environment builder's `Keep` set, so a re-run of
  **Lab ▸ Environment ▸ Build Prototype Environment** leaves them alone. **A new valuable belongs
  under that root** — a loose one at scene root, or anything under `Props`, is destroyed by a rebuild.

### On-screen text

All placeholder HUD goes through `Hud` (`Assets/Scripts/Hud.cs`) — never raw `GUI.Label`, whose
12px dark-grey default is unreadable against a night field. `Hud.Row(n, text, tint)` for corner
readouts, `Hud.CentrePrompt(text)` for the interaction prompt. Sizes scale with screen height.

Row numbers are claimed and must not collide. **Left column** (`Hud.Row`): **0-1** TimeOfDay,
**2** Generator, **3** PlayerVitals, **4** MonsterSpawner, **5 and down** `MonsterDebugHud`
(last noise, then one line per monster — set its `firstRow` if you need row 5 back).
**Right column** (`Hud.RowRight`):
**0** money balance, **1** the `+$100` change popup, both drawn by `MoneyHud`. The two columns are
numbered separately, so they cannot collide. Claim the next free number for a new readout.

## C# conventions

- Public tunables as plain public fields (matches existing scripts) so they're editable in the Inspector.
- Cache `GetComponent<T>()` in `Awake`/`Start`. Never call it per frame.
- Prefer serialized references over `GetComponentInChildren`/`transform.GetChild` lookups for anything set up in the Editor.
- Multiply per-frame movement and rotation by `Time.deltaTime`.
- If a null reference would break gameplay, `Debug.LogError` rather than failing silently.
- Initialize events as `= delegate { }` to avoid null checks.
- Never silence a compiler warning with `#pragma warning disable` — fix the cause.
- Match the surrounding comment style: short, explains *why* (e.g. "so it matches mouse look"), not *what*.

## Don't

- Enter or exit Play mode, or trigger a build, unless asked.
- Reformat or restructure files you weren't asked to touch.
- Add packages to `Packages/manifest.json` without asking.
- Delete assets or scene objects that weren't part of the request.
