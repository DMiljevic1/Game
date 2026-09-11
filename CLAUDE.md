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

- Roots: `Directional Light`, `Global Volume`, `Player` (CharacterController + `PlayerMovement`, child `Main Camera` + `MouseLook`), `Systems`, `Generator`, `FuelCans`, `PlayerRespawn`, `Ground`, `Boundary`, `House`, `Forest`, `Props`, `SellStation`, `Store`, `LootSpawnPoints`.
- **The environment is generated, not hand-placed.** `Assets/Editor/PrototypeEnvironmentBuilder.cs`
  (menu **Lab ▸ Environment ▸ Build Prototype Environment**) rebuilds `Ground`, `Boundary`, `House`,
  `Forest`, `Props` and `LootSpawnPoints` from scratch out of primitives. Change the level by editing that script and
  re-running it, not by dragging cubes — a re-run destroys every root outside its `Keep` set
  (`Player`, `Systems`, `Directional Light`, `Global Volume`, `Generator`, `PlayerRespawn`, `FuelCans`,
  `Flashlight`, `SellStation`, `Store`, `Valuables`, `PatrolRoute`, `Monster_Test`),
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
in UI. `Wallet.HasAuthority` is the pattern — a seam that becomes `IsServer` when Netcode lands.

### Level 1 — bring Tom's case home

The whole objective, and deliberately nothing more: **find Tom's case at his camp in the deep woods
and open it at the kitchen table.** No quest UI, markers, stages, timer or scripted events — the
tension comes from the systems (fuel, depth, weight, noise). The loot/store loop runs alongside it
untouched, and the level can be finished with every optional story element ignored.

| Piece | Owner | Where |
|---|---|---|
| Objective state, spawning the case, completion | `Expedition` (authority, `Instance`, `HasAuthority`) | Systems |
| The case | `TomsCase` (a `Carryable`, **not** a `Valuable`) | `Assets/Prefabs/Objective/TomsCase.prefab`, spawned at run time on `Props/TomsCamp/CasePoint` |
| Where it is opened | `CaseTable` (`IInteractable`, **E**) | `House/Furniture/Kitchen/KitchenTable`, added by the environment builder |
| The ending screen | `LevelCompleteHud` (pure observer) | Systems |
| The one mandatory note | `Readable` on `House/Detail/Notice`, drawn by the player's `ReadableHud` | builder / Player |

- **The case is the Large load.** `Carryable.ApplyLargeLoad` is the one copy of the Large numbers,
  shared with `ValuableSize.Large`: 3.1 walk (slower than chase 3.6), no sprint, no pack — so no
  torch in hand while carrying it. Retune it with the television and `chaseSpeed`, never alone.
- **Taking it is loud, once.** The first lift emits `liftNoiseRadius` (30 m) from where it lay — an
  ordinary `Noise`, a lure, never a spawn. Estimated ~1.2 of the 5 monsters are in earshot at a
  random moment (26% none, 33% two or more). Picking it up again after a drop is as quiet as anything.
- Everything else it needs already existed: dying drops it where you fell, the sell counter ignores
  it (not a `Valuable`), and the carrying line says `(too heavy to run)`.
- `CaseTable` offers **E "Open Tom's case"** only while you hold it — the generator-refuel pattern —
  then `ConsumeCarried`, `TomsCase.OpenAt` on its `CaseRest`, `Expedition.CompleteLevel`. There is no
  scene change yet: `LevelCompleteHud` shows the map for `fullSeconds`, then shrinks to
  `Hud.RowRight(2)`. The map is deliberately vague about "2" — Level 2 is not decided.
- **Tom's camp** is built by `PrototypeEnvironmentBuilder.BuildTomsCamp` on the first clear spot near
  `CampTarget` (−18, −38): currently (−18.4, −38.4), 41 m from the generator, a 43 m `PathComplete`
  walk from the entrance (~14 s carrying the case). Its dressing is render-only and **unlit** — the
  deep forest still has no lights.
- **Environmental story, all optional:** the notice's sketch → the wreck's headlight → pale
  `Env_Paint` bands on trunks every ~4 m (`PaintTrail`; faint by moonlight, plain by torch) → a
  painted X on the tree by the camp. `TrampledRing` wears dirt strips at the generator's radius (read
  from the component) — the safe-zone rule on the ground. Nina's drawing in the shed is a second,
  optional `Readable`.
- **`Readable` holds no state.** Which note is open is the reader's own `ReadableHud`, so two players
  can read the same notice. It closes when you walk `closeDistance` away; reading never pauses the
  game. A new note is a collider, a `Readable` and its text.
- **Known risk:** nothing tells the player the kitchen table is where the case goes. Watch for it in
  playtests before adding anything.

### TimeOfDay — Level 1 is always night

**There is no day/night cycle.** Level 1 is permanently night: no phases, no clock, no time
progression, no phase events. `Assets/Scripts/TimeOfDay.cs` on `Systems` sets the night's light
once in `Awake` — the moon (`sun`, `nightIntensity`, `nightColor`, `moonYaw`, `moonElevation`) and
the Trilight ambient (`nightAmbientColor` × `nightAmbient`) — and nothing ever changes it.

- Nothing may keep a timer that stands in for the old cycle. Anything that happens "at night"
  happens from level start (see `MonsterSpawner`).
- `AmbientSky` / `AmbientEquator` / `AmbientGround` are the base ambient; `NightDepth` scales them.
- `ApplyLighting()` is public so the environment builder can preview the night in edit mode.
- The night is fixed, so there is nothing to replicate: every co-op client lights its own scene.

### Generator — the night's pressure

`Assets/Scripts/Generator.cs` on the `Generator` object beside the country house.

- Ask **`Generator.IsPointProtected(pos)`** — never test a radius by hand. Monsters must respect it.
- Fuel drains in real time (`burnRate` × `Time.deltaTime`) while it runs.
- Tuning as of now: tank 100, 0.25/s = **400s (6:40) on a full tank**. **Level 1 starts it off, at
  40 fuel (160 s)** — set on the scene's Generator; the environment builder never touches `fuel` or
  `burnRate`, so an Inspector retune survives a rebuild. The night never ends, so once the tank is
  dry the light stays off until someone refuels it.
- `IsInsideAnyRadius(pos)` asks the radius **whether or not it runs** — for decisions about the
  ground (where monsters may spawn), never about safety right now. Safety is always `IsPointProtected`.
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
| **E** | open / close doors, sell at the counter, open / close the store, revive a body, read a note, **and** open Tom's case | `DoorInteraction.useKey`, `SellStation.sellKey`, `Store.browseKey`, `PlayerBody.reviveKey`, `Readable.readKey`, `CaseTable.openKey` |
| **F** | pick up an item, and pour fuel in | `Carryable.pickUpKey`, `Generator.refuelKey` |
| **Q** | drop what you are holding | `PlayerInteractor.dropKey` |
| **T** | start / stop the generator | `Generator.powerKey` |
| **X** | switch the torch on / off | `Flashlight.toggleKey` |
| **1–4** | select a pack slot | `PlayerInventory.slotKeys` — **not** routed; see below |

`PlayerInventory` reads 1–4 itself, and that is not a violation of the one-router rule: a slot is
not something you aim at, so it is a player system like `PlayerMovement`, not an interactable. They
are in this table so nothing else ever claims them — an interactable that declared `Alpha1` would
fire alongside a slot swap.

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
  `PlayerVitals` records `LastDeathPosition` before `OnDied` fires, and the body is left on the
  same spot (see *Death and revive*). Co-op: the item goes back to the world rather than to
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

### Inventory — four slots on your back, one thing in your hands

`PlayerInventory` on the Player, in the Lethal Company shape. It owns the four slots and the
selection **and nothing else — the hands still belong to `PlayerInteractor`.** There is exactly one
`carried` reference in the game and it is not in here.

- **Nothing is ever cloned, destroyed or respawned to move an item.** Stowing is
  `interactor.ConsumeCarried()` (which already meant "out of the hands without placing it") plus
  `Carryable.OnStowed`, which just deactivates the GameObject. It is the same object with the same
  script, value, name and state all the way through GROUND → HAND → SLOT → HAND → GROUND.
- **`HeldFromSlot` is derived, never notified:** it is valid only while the hands still hold the very
  item the pack handed over. So a Q drop, a sale, a death or a new pickup all sever it for free.
  Do not replace this with a notification — a future item route would eventually forget to call it.
- `Carryable.IsHeld` means "in the player's possession", hands **or** pack, so every existing
  "still on the floor?" guard keeps working. `IsStowed` is the narrower question.
- **The hands are never emptied to make room.** `PlayerInteractor.Carry` stows what you hold, and if
  the pack is full too it *refuses the pickup* — `CanPickUp` is the seam, and `PickUpRefusal` puts
  the reason on the prompt, because being unable to pick something up with no explanation reads as
  a bug. With no `PlayerInventory` wired, `Carry` drops what you hold exactly as it did before.
- **Equip vacates the slot first.** That frees somewhere for whatever is already in the hands, which
  is what lets a swap work with all four slots full and nothing hitting the floor.
- **Unequipping and dropping are two different actions, and no number key ever becomes a drop.**
  Selecting an *empty* slot unequips: the held item goes back to **the slot it came from** if it has
  one (pressing 2 while holding what came out of 1 puts it back in 1 and leaves 2 empty), and
  otherwise — it came off the ground — it is stored in the empty slot just selected. That second
  case is how something gets *deliberately* stored rather than only by being bumped out of the hands
  by the next pickup. Once an item is in the pack it stays in the player's possession until they
  press **Q**; `PlayerInventory` must never call `DropCarried`.
- **Auto-stow prefers the slot the item came from** when that slot is still free, and only then the
  first empty one: taking the watch out of slot 3 and picking something up should not shuffle the
  watch to slot 1.
- **`Carryable.canBeStoredInInventory` is the whole "too big for the pack" rule** — per item, never
  by name. A cleared one can be picked up and carried normally but can never occupy a slot, so
  `CanStowCarried` is false, `CanPickUp` is false, and you cannot pick anything else up until you
  put it down. A number key with one in your hands does nothing at all: it is not swapped away and
  it is *not* dropped for you. `ValuableSize.Large` clears it through the shared
  `Carryable.ApplyLargeLoad`, so the television (and Tom's case) are automatically hands-only —
  retune that with the Large movement numbers, not separately. `Custom` leaves it as authored.
- `PlayerInteractor.PickUpRefusalReason` distinguishes the two refusals ("hands and pack full" vs
  "put the old CRT television down first") because they need different actions from the player.
  `Carryable.PickUpRefusal` puts it on the prompt for every item for free.
- A stowed torch goes dark and lights again when taken out, because stowing deactivates the object.
  Override `OnStowed`/`OnUnstowed` for an item that should keep running in the pack.
- Dying still drops only what is **in the hands**; slots survive a death. Change that in
  `PlayerInteractor.DropCarriedOnDeath`, not here.
- `InventoryHud` on the Player only *reads* the pack — no slot, key or swap logic lives in the UI,
  the same rule `MoneyHud` follows. Being immediate-mode it is redrawn every frame, so it cannot go
  stale. `Hud.BottomBarHeight` is the one number reserving the bottom strip; the interactor's
  "Carrying:" line sits above it, so the two cannot drift into each other.

### Economy as of now

Generator tank 100 at 0.25/s, so a full tank runs **400 s**. A can holds **40** = **160 s** more. The
level starts at **40 fuel (160 s), switched off**; the four placed cans add **640 s** (two in the
light, two at its edge), and after that fuel comes only from the store at **$50 a can**. So the loop
is loot → money → fuel/equipment → further out. The night has no end, so refuelling is a recurring
trip, not a one-off errand. Retune these together, never one alone — and against the haul (see
*Loot spawning*), which is **not balanced yet**.

### Monsters

`MonsterSpawner` on `Systems` spawns one wave of `waveSize` (4) in `Start` and never clears it — it
is always night, so they are out from the first frame. The hand-placed `Monster_Test` is extra to
that wave. `Assets/Prefabs/Monster.prefab`. Spawn points avoid the generator's radius through
`Generator.IsInsideAnyRadius`, not `IsPointProtected`: the generator starts the level off, and a
wave spawned in the yard before anyone could start it would be no choice at all.

**The monster is blind.** It has no vision and never touches the player's transform — the only way
anything reaches it is `Noise` (see below). `Monster` implements `INoiseListener`; there is no
`target` field, and `SetTarget` is a deliberate no-op kept so the spawner needn't know what it spawned.
**Never** add a raycast-to-player, a distance-to-player check, or a light/flashlight test to it —
that is the whole design, not an optimisation. It hurts what it *touches*, found by
`OverlapSphereNonAlloc`, so even the attack never asks where the player is.

**State machine** (`MonsterState`, one per frame in `Update`):

| State | Does | Leaves when |
|---|---|---|
| `Patrol` | Roams the map at `patrolSpeed`, destinations chosen by `MonsterPatrol` | any noise is heard |
| `Alerted` | Walks to the (blurred) noise position at `alertSpeed` | arrives → `Investigate`; louder/repeated noise → `Chase` |
| `Investigate` | Stands and sweeps its look around for `investigateDuration` | timer out → `Patrol` (agitation reset); new noise → `Alerted`/`Chase` |
| `Chase` | Same as `Alerted` but `chaseSpeed`, always re-targeting the **newest** noise | `agitation` decays below `chaseThreshold` → `Alerted` |

- **`agitation`** is the aggression dial: each heard noise adds `agitationPerSound * (0.5 + clarity)`,
  it decays at `agitationDecayPerSecond`, and crossing `chaseThreshold` is what turns walking into
  running. Sounds close together stack; silence calms it down.
- Monsters must **never** decide safety by measuring distance themselves; they call
  `Generator.GetProtector` / `IsPointProtected`. `KeepOutOfSafeZone` runs on every heard position
  *and* every destination, so a noise made inside the light draws it to the boundary and no further.
  `Monster.Move` also re-checks after every move and pushes back out, so no collision slide or
  steering bug can ever put one inside the light.
- **Moving is still the monster's own job; only the route is the NavMesh's.** `NavPathFollower`
  (a plain class, one per monster) turns "go there" into "steer at this corner", and `Monster.Move`
  still drives the `CharacterController` — so gravity, the safe-zone push-out and attack-by-touch
  all stay in one place. There is deliberately **no `NavMeshAgent`**: an agent would own the
  transform and fight all three. Used by pursuit as well as patrol, so chasing rounds corners too.
  `NavPathFollower.Blocked` is the "you cannot get there from here" signal — it is true only when
  both ends sampled onto the mesh and the route still came back incomplete.
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
- **Known limit:** sound is not occluded. A noise through a wall carries as far as one in the open.
- **Known limit:** only a *hunting* monster forces doors, so a patrolling one that routes through a
  shut doorway walks into it, times out and picks somewhere else. Correct, but it costs a leg.

### MonsterPatrol — where it goes when nothing has its attention

`Assets/Scripts/MonsterPatrol.cs` is a component on the monster and **knows nothing about
detection** — not hearing, not sight, not agitation, not the player. Hand it a position, get back a
walkable point. That ignorance is the point: a sighted monster can drop the same component on and
roam identically without either script sharing a line of the other's detection code. Detection
always wins; `Monster` simply stops asking while it is alerted, investigating or chasing.

- **It should read as "out walking", never as "waiting to be triggered".** Legs measure 18–68m
  (`minTravelDistance` 16 is the floor that stops it dithering in one corner), and `pauseChance`
  0.3 means most arrivals roll straight on. Measured over 25 legs: ~920m walked, ~12s paused.
- `directionSpread` (130°, not 180°) is what makes a route look like a route — a new leg sets off
  roughly onward rather than doubling back. Near the rim of `areaRadius` the basis flips inward so
  it does not keep aiming off the edge of the world.
- **A destination is only taken if `NavMesh.CalculatePath` returns `PathComplete`**, so it never
  sets off somewhere it cannot reach. Each of `maxAttempts` relaxes the distance and the spread a
  little, so a monster shut in a small room still finds somewhere legal instead of failing perfectly.
- It also refuses anything inside `Generator.IsPointProtected`, and `Monster` re-checks the current
  destination every frame in case the generator started mid-leg.
- **A leg is abandoned three ways**, all in `Monster.PatrolLegLost`: the follower reports `Blocked`,
  actual `controller.velocity` stays under 30% of `patrolSpeed` for `patrolStuckTime`, or the leg
  outlasts `patrolSecondsPerMetre` × its length. Any of them picks somewhere else — nothing leans
  on a wall for the rest of the night.
- `anchors` (filled from `MonsterSpawner.patrolRoute`, i.e. the `PatrolRoute` root) are a **bias,
  not a loop**: `anchorChance` 0.25 of legs head for one. `Monster.SetPatrolRoute`'s `startIndex`
  argument no longer means anything and is kept only so the spawner needs no change.
- **Nothing turns on the spot.** Facing follows travel and only travel; a patrol pause holds the
  heading it arrived on. The one place a monster turns while standing is `Monster.Scan` during
  `Investigate`, and that is a sweep — turn to a direction, hold `scanHoldTime`, choose another —
  because a continuous spin reads as a bug rather than as searching.
- With no NavMesh baked it logs a warning once and falls back to the old straight-line circle, so
  a scene without a bake degrades rather than freezing.

### The NavMesh

`NavMeshSurface` on **`Systems`** (in the environment builder's `Keep` set), baked to
`Assets/Scenes/SampleSceneNavigation/NavMesh-Systems.asset`. Collect **All**, geometry **Physics
Colliders** (canopies and bushes are render-only, so they correctly do not block), voxel size 0.1
because the door openings are only 1.2 wide.

- `NavMeshModifier` with `ignoreFromBuild` marks everything that must **not** carve a hole: `Player`,
  `Monster_Test`, and the carryable roots `FuelCans` / `Flashlight` / `Valuables`. A door hinge
  carries one too — a mesh baked around a shut door would leave every room an unreachable island.
- **`Lab ▸ Environment ▸ Build Prototype Environment` re-bakes at the end** (`RebakeNavMesh`), so a
  rebuilt forest does not leave monsters routing around trees that are gone. `House` is *not* in the
  `Keep` set, so the doors' modifiers are re-added by `PrototypeEnvironmentBuilder.Door`.
- Anything else that changes level collision needs a re-bake. Verify with coverage and a
  `CalculatePath` probe (the whole 110×110 ground samples, and every house room is `PathComplete`
  from outside) rather than by eye.

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

### Death and revive

Death is a **state**, not a teleport. A dead player stays dead until someone brings their body
home and uses **Adrenaline** on it. The pieces are deliberately separate, and each is replaceable:

| Concern | Owner | Where |
|---|---|---|
| Alive / dead, health, `ReviveAt`, `Respawn` | `PlayerVitals` | Player |
| Controls, avatar colliders and renderers off while dead | `PlayerDeathLock` (pure observer) | Player |
| The body left in the world | `PlayerBody` (a `Carryable`) | `Assets/Prefabs/PlayerBody.prefab`, spawned at run time |
| Revival item | `Adrenaline` (a `Carryable`, `IStorePriced`) | `Assets/Prefabs/Store/Adrenaline.prefab` |
| Revive rules, the revive itself, bodies, price curve | `Revival` (authority, `RevivePricing`) | Systems |
| Revives used this run, who is alive, all-dead | `RunState` (authority) | Systems |
| "The safe house" | `ReviveZone` (a box, not a collider) | `House`, generated by the environment builder |
| What singleplayer does when everyone is dead | `SingleplayerDeathFallback` | Systems — **delete when co-op lands** |

- **`PlayerVitals` knows none of the others.** It fires `OnDied` / `OnRevived`, plus the static
  `AnyDied` / `AnyRevived` for run-level listeners, and keeps a static `All` registry of players (a
  scene registry like `Generator`'s, not per-player state). Nothing else writes health.
- **The body is its own object, not the player's.** So the player object is free to become a
  spectator later, and the body is an ordinary hands-only `Carryable` (television weights: 0.62 walk,
  no sprint, no pack). Carrying a teammate home is the same pick-up/put-down as anything else; a
  future body teleporter only has to move an un-held body's transform — `Revival` never asks how it got there.
- **`Revival.ReviveRefusal` is the single definition of "allowed"**, read by both the prompt and
  `TryRevive`: body on the ground, inside a `ReviveZone`, rescuer holding `Adrenaline`, rescuer alive.
  The body's pickup prompt always says which one is missing.
- **The counter is global for the run and lives on `RunState`.** It resets in `Awake`, so every level
  load is a new run; `BeginRun()` exists for a new run without a reload. Only `Revival.TryRevive`
  calls `RecordRevive`, and only after a successful revive.
- **Price = `RevivePricing.PriceFor(revivesUsed + unspent Adrenaline)`.** Counting unspent doses
  (`priceIncludesUnspentAdrenaline`) is what stops a team buying four at $500 before anyone dies.
  Retune the `prices` list on `Revival`; past its end each revive costs `growthPastList` × the last.
- A revive stands the owner up at the body with `reviveHealthFraction` (0.5) — the house regenerates
  the rest. The body and the dose are destroyed; the dose leaves `Adrenaline.UnspentCount` immediately.
- `PlayerDeathLock` re-disables the controls **every frame** while dead, because the store restores
  what it suspended when it closes, and dying with the store open would otherwise hand a corpse its legs back.
- **Singleplayer:** every death is "all players dead". `SingleplayerDeathFallback` listens to
  `RunState.OnAllPlayersDead` and after `standInDelay` (4 s) `Respawn`s the player at the house to
  act as their own rescuer. The body stays, so fetch → buy → revive is fully playable. In co-op this
  component is removed and `OnAllPlayersDead` becomes Game Over.
- One body per player: a new death replaces that player's old body (only reachable in singleplayer).
- What is in the hands still drops where you fell; **pack slots survive a death**, as before.

### Money and selling

`Wallet` on `Systems` is the **single authority on money** — the same shape as `RunState`, with a
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

| Size | Walk | Sprint | Pack | Items now |
|---|---|---|---|---|
| `Small` | 5.0 (×1) | 9.0 (×1) | yes | old radio $40, camera $60, old clock $75 |
| `Medium` | 4.5 (×0.9) | 8.1 (×0.9) | yes | laptop $150 |
| `Large` | 3.1 (×0.62) | **none** | **no** | old CRT television $300 — and Tom's case (not loot), through the same `Carryable.ApplyLargeLoad` |

- The Large numbers are chosen against the monster: chase speed is **3.6**, so 3.1 means the
  television is the one thing you cannot outrun. That is the risk/reward, not a balance accident —
  retune `chaseSpeed` and the Large preset together.
- Two things fall out of this for free and should be left alone: `NoiseEmitter` reads the
  controller's *actual* speed, so carrying the television is also **quieter** than running; and
  `PlayerInteractor`'s carrying line says `(heavy)` / `(too heavy to run)`, because being slow with
  no explanation reads as a bug.
- The five items are **prefabs** in `Assets/Prefabs/Loot/`, built from primitives like the rest of
  the prototype. They are not placed in the scene: `LootSpawner` instantiates them at run time under
  the `Valuables` root, which is in the environment builder's `Keep` set and is empty in the Editor.

#### The store — money back into equipment

`Store` on the `Store` root (a counter in the house **Entrance**, against the south wall between the
doormat and the shoe rack) is the mirror image of `SellStation` and follows all of its rules: an
ordinary `IInteractable` declaring **E**, the `Wallet` still the single authority on money, and a
`HasAuthority` seam for netcode. `StoreHud` beside it only *reads* — no price, stock or arithmetic
lives in the UI, the same rule `MoneyHud` and `InventoryHud` follow.

- **A new purchasable is a prefab and one number.** Add a `StoreItem` to `stock`; there is no
  per-item script, exactly as a new `Valuable` is a prefab and two numbers.
- **The bought item reaches the player through `PlayerInteractor.Carry`** — the one existing route
  into their possession. So it already stows what they were holding into a free slot, and already
  refuses when hands *and* pack are full. `Store` adds no inventory API and holds no item reference.
- **Money is spent only after the item is certain to have somewhere to go** (`interactor.CanPickUp`
  is checked first), so a refused pickup can never leave the player poorer with nothing to show.
  `Wallet.Add(-price)` does the paying, and `MoneyHud` already renders the negative delta.
- **Nothing sold here can ever be found as loot.** That is not a flag: `LootSpawner` only ever
  instantiates from `lootPrefabs`, which holds `Valuable`s, and store goods are neither `Valuable`s
  nor in that list. Keep it that way — the two catalogues must not be merged.
- **Buying uses the mouse, so the store is modal.** `Open` unlocks the cursor and switches off
  `MouseLook` and `PlayerMovement`, and `Close` gives back **only** what it took (a script already
  disabled is left alone). Neither of those scripts knows the store exists. `Update` also closes the
  panel if the browser stops existing — a cursor locked away by a vanished shop is the worst thing
  to leave behind. **E** or **Escape** closes it.
- The counter's `Shop_Backboard` reaches **y 2.6** for the `Counter_Backboard` reason: a waist-high
  cabinet alone sends the aim ray over the top into the wall. Don't shorten it.
- Purchases emit `Noise` themselves (8 m). The interactor's 12 m noise only covers keys *it* routed,
  and a button click is not one.
- The panel draws centred, so it claims no `Hud.Row` / `Hud.RowRight` number. `Hud.Button` is the
  shared style for its buttons — the only place in the game the mouse is used.

**Stock as of now** (prefabs in `Assets/Prefabs/Store/`, built from primitives like everything else):

| Item | Price | What it is |
|---|---|---|
| Flashlight | 60 | The existing `Flashlight`: beam range 32, spot 42°, intensity 70 |
| Better Flashlight | 180 | The same script, stronger beam: range **55**, spot **58°**, intensity **160**, whiter |
| Shovel | 90 | A plain `Carryable`. No behaviour yet — the gameplay comes later |
| Adrenaline | **the next revive price** (500 → 750 → 1,100 → 1,500 …) | `Adrenaline` prices itself through `IStorePriced`; see *Death and revive* |
| Fuel can | **50** | A detached copy of the scene cans (`FuelCan`, 40 fuel = 160 s). The only fuel once the four placed cans are used |

- `StoreItem.CurrentPrice` is what is shown and charged, never `price` directly. A prefab that
  implements `IStorePriced` works its own price out; everything else uses the listed number.
  `TryBuy` reads the price **once, before spawning**, because Adrenaline is dearer the moment it exists.

- **The torch is store-only and no longer lies on the house floor.** The `Flashlight` scene root was
  removed when the store was added; buying one is how a run gets a light. Don't re-place one by hand.
- Every store prefab sets `NavMeshModifier.ignoreFromBuild`, like every other carryable, so a dropped
  one never carves a hole in the mesh.
- Prices were set against the old ~$501 haul. With the guaranteed Large piece it is now **~$727**, of
  which only **~$103** lies inside the generator's light. **Not rebalanced yet** — retune against a
  playtest, and never one price on its own.

#### Loot spawning — rarity is derived, position is shuffled

`LootSpawner` on `Systems` rolls a run's loot in `Start`, `HasAuthority`-shaped like `MonsterSpawner`.
It owns two decisions and nothing else. **Never place a valuable in the scene by hand** — add a
prefab to `lootPrefabs` instead, or nothing will know it exists.

- **There is no rarity number anywhere, and there must never be one.** `SpawnWeight` derives it from
  the item's own `value` and `size`: `(commonValue / value) ^ valueExponent`, times `mediumSizeWeight`
  (0.7) or `largeSizeWeight` (0.45). So a new valuable is still *a new prefab and two numbers*, and
  **repricing an item automatically re-rarities it** — never tune price and frequency apart.
- `minimumWeight` (0.01) is a floor, so nothing can ever become unfindable however dear it gets.
- `MaxPerRun` is derived from size too (Small 3 / Medium 2 / Large 1), so the cheap stuff cannot
  flood the map and there is never more than one television.
- **At least `minLargePerRun` (1) Large piece a run.** `GuaranteeLarge` draws it from the table by
  weight among the Large kinds — *which* one is still derived, only *whether* is fixed — and it
  replaces the cheapest piece on a Large-capable spot, so the item count is unchanged. Then the
  **first Large piece, however it was drawn, moves beside `focus`** (Tom's camp, within
  `focusRadius` 15 m), so the prize and the case always compete for the same trip. Never a scripted spawn.
- **Depth re-pairs; it never re-rolls.** The table and the shuffle choose the same items and spots as
  always; `AssignByDepth` then sends dearer pieces to spots further from `depthCentre` (the
  generator), blurred by chance by `1 − depthBias` (0.75). So rarity, caps and separation are
  untouched, and `depthBias` 0 is the old behaviour. The builder wires `depthCentre` and `focus`.
- **Measured over 2,000 runs of the real `SpawnRun`** with the defaults (`commonValue` 40, exponent
  1.4, 6–9 items a run, averaging 7.5):

  | item | price | size | avg/run | runs containing it | avg distance from generator |
  |---|---|---|---|---|---|
  | old radio | 40 | Small | 1.88 | 96.4% | 16 m |
  | camera | 60 | Small | 2.24 | 96.4% | 27 m |
  | old clock | 75 | Small | 1.88 | 92.0% | 39 m |
  | old CRT television | 300 | Large | 1.00 | **100%** (by the camp in 100%) | 40 m |
  | laptop | 150 | Medium | 0.51 | 42.4% | 44 m |

  Average haul **$727**; only **2.2 items (~$103)** lie inside the generator's 22 m. The laptop's
  42% matches the unguaranteed table's 43% — the check that depth changes where, not what. The caps
  still compress the cheap items towards each other (the raw curve is 47/27/20 per draw); that is
  the caps doing their job. **Not balanced yet.**
- **`TryResolveSurface` is the single definition of a valid spot** and the editor tool calls this
  same runtime method rather than reimplementing it, so what is authored and what is used cannot
  drift. It raycasts down, rejects faces steeper than `maxSurfaceSlope`, then overlap-boxes
  `fitProbeSize` (sized for the television) above the hit. **The box is what catches a point inside
  a wall** — a downward ray that *starts* inside a wall collider passes straight through it and finds
  the floor beneath, so the ray alone would happily bury an item in masonry.
- Items are then lifted so their **renderer bounds' bottom** sits on the surface: a pivot is not
  always a base, and the fuel cans and torch are added to the taken-spots list so nothing spawns
  inside them.
- Randomisation is a real **Fisher-Yates shuffle** of the points, not a random start index into a
  fixed order, and `minItemSeparation` (3m) keeps two pieces out of the same spot. `useFixedSeed` is
  off by default; when on it saves and restores `Random.state` so it cannot knock `MonsterSpawner`
  off its own stream.
- `Log spawn odds` on the component's context menu prints the table, so the curve is never a black box.

`LootSpawnPoint` is a bare marker — no item, no odds, no state. `allowLargeItems` is the one dial
(cleared above a 1.0m surface, so a television is never balanced on a shelf). `Assets/Editor/LootSpawnPointBuilder.cs`
(**Lab ▸ Loot ▸ Rebuild Loot Spawn Points**) generates the `LootSpawnPoints` root and
**`Build Prototype Environment` re-runs it**, so a rebuilt house never leaves points in its new walls.

- It samples a jittered grid over the whole house footprint plus three rings round the house (yard
  9–20m, field 20–34m, deep 34–46m) and one round Tom's camp (3.5–14m, found at `Props/TomsCamp` —
  the woods there are dense, and the deep ring alone left two Large-capable spots), and keeps
  whatever survives the probe. **It deliberately does not describe the rooms** —
  the probe rejects walls, partitions and furniture on its own, so what remains is exactly the
  walkable floor and the tops of the furniture.
- **Each region has its own cap.** A single shared budget is spent by whichever region is sampled
  first, which left the far field with zero points — and the far field is most of the reason to
  leave the house.
- Expanding means adding a region there, or dropping a `LootSpawnPoint` in by hand: the spawner
  takes every one it can find, wherever it is parented.

### Lighting — a night you can walk by, woods that get darker

The goal: without a torch you can navigate and read the house, trees, paths and props, and it
still feels like night; the deeper into the woods, the darker, until the torch is what you see by.

- **`TimeOfDay` owns the moon and the ambient.** There is one directional light, fixed as the moon
  (`moonYaw`, `moonElevation`), so the canopy still throws shadows. There is no sun and no day.
- **Ambient is a Trilight gradient, not the skybox.** The night sky is near-black, and skybox ambient
  only refreshed on `DynamicGI.UpdateEnvironment`, so writing `ambientIntensity` did nothing
  reliable. Gradient colours apply immediately. `TimeOfDay` forces the mode; don't switch it back.
  Equator is 0.7 of the sky colour and ground 0.3 — the strong equator is what lets trunks, walls
  and canopies read, because with near-black albedo a weak one leaves them as cut-outs.
- Night tuning (set by the environment builder): moon **0.55**, `nightAmbient` **1.8** × (0.30, 0.38,
  0.60). These look high because every albedo in the level is 0.035–0.1. Retune against captures,
  not by eye in the Inspector.
- **`NightDepth` on `Main Camera` is the whole "further from the house, darker" gradient**, measured
  from the `Generator` with a smoothstep from `clearRadius` 18 m to `deepRadius` 45 m. It scales the
  ambient down to `deepAmbient` (0.3) and thickens fog from 0.022 to 0.055. **Ambient does most of
  the work on purpose:** fog dims the torch beam as much as the moonlight, so darkening the woods
  with fog alone would make the torch useless exactly where it is needed. It is a per-viewer visual
  (nothing in the simulation reads fog or ambient), so in co-op each client runs its own. Each
  `LateUpdate` it writes `TimeOfDay`'s base ambient × its depth factor, never a multiple of what is
  already in `RenderSettings`, so it cannot compound — never write ambient from anywhere else.
- **Where the light is, and deliberately isn't.** The generator circuit (house lamps, work and porch
  lights) is steady and switched by `Generator`. Three old lamps on their own dying supply carry
  `LightFlicker`: `Props/Lamps/PathLamp` where the path leaves the trees, `Props/Shed/ShedLamp`, and
  `Props/WreckedCar/Car_Headlight` (a spot staring into the woods). `Props/Gen_StandbyLamp` is a
  small steady amber lamp that stays lit when the fuel runs out — the moment you most need to find
  the generator. All of them sit within ~25 m of the house; **the deep forest has none, and should
  keep having none.** Never put lights on trees.
- `LightFlicker` never goes on a generator-powered light (protection must read as steady), and it
  draws from its own `System.Random`, so a lamp can never shift the stream the spawners use.
- Window panes cast no shadows, so a lit room throws window-shaped light onto the yard. The boards
  over the boarded windows still cast, which is what slats that light.
- Shadow budget: a shadowed point light costs six atlas slices, so the old lamps are shadowless and
  only the headlight (a spot, one slice) casts. `PC_Renderer` is Forward+, so there is no per-object
  light limit — which matters, because the whole ground is one cube.
- Measured at night from edit-mode renders (share of near-black pixels, before → after): yard 71% →
  30%, by the shed 69% → 20%, deep forest 97% → 93%. The deep forest is meant to stay dark.

### On-screen text

All placeholder HUD goes through `Hud` (`Assets/Scripts/Hud.cs`) — never raw `GUI.Label`, whose
12px dark-grey default is unreadable against a night field. `Hud.Row(n, text, tint)` for corner
readouts, `Hud.CentrePrompt(text)` for the interaction prompt. Sizes scale with screen height.

Row numbers are claimed and must not collide. **Left column** (`Hud.Row`): **0-1** free (the old
clock readout), **2** Generator, **3** PlayerVitals, **4** MonsterSpawner, **5 and down** `MonsterDebugHud`
(last noise, then one line per monster — set its `firstRow` if you need row 5 back).
**Right column** (`Hud.RowRight`):
**0** money balance, **1** the `+$100` change popup, both drawn by `MoneyHud`, **2** the
`LevelCompleteHud` line once the map shrinks away. The two columns are numbered separately, so they
cannot collide. Claim the next free number for a new readout.

Centred panels — the store, `ReadableHud`'s note, `LevelCompleteHud`'s map — claim no row. Dark text
on a paper panel uses `Hud.Ink` (no shadow, which would only smear it), and wrapped body text uses
`Hud.Paragraph`.

The **bottom** of the screen is not row-numbered: `Hud.BottomBarHeight` reserves a strip for the
inventory bar (`InventoryHud`), and anything else drawing down there keeps clear above it, as the
interactor's "Carrying:" line does. `Hud.Box` and `Hud.Frame` draw the panels — one shared 1×1
texture, so a panel costs no allocation per frame.

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
