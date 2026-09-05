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

- Roots: `Directional Light`, `Global Volume`, `Player` (CharacterController + `PlayerMovement`, child `Main Camera` + `MouseLook`), `Level`, `Ground`, `Boundary`.
- `Level` holds the building as scaled cube primitives named `<Room>_<Part>`: rooms `A`, `B`, `C` west→east along +x, sharing walls at the `AB_`/`BC_` seams.
- **Geometry reference points:** floor tops sit at **y = 0.05**; walls are 3.0 tall, **0.15 thick**; roofs at y = 3.08; door openings are **1.2 wide × 2.2 high** with an 0.8-tall lintel centered at y = 2.6.
- `Ground` is a scaled cube (not a Plane) with its top at **y = 0.04** — 1 cm under the floors, so overlapping faces don't z-fight.
- `Boundary` holds four **collider-only** `GameObject`s (no `MeshRenderer`) at the grass edges. They are derived from `Ground`'s renderer bounds — if the ground is resized, rebuild them from those bounds rather than hardcoding numbers.

### Door pattern — follow it exactly

A door is four objects: two jambs, a lintel, and a hinge.

- The hinge is an **empty `GameObject` at the opening's edge** carrying `DoorInteraction`; the door cube is its **child** at `localPosition = (0.6, 0, 0)` with `localScale = (1.1, 2.2, 0.15)`.
- `DoorInteraction` must be on the hinge, never on the door mesh — it rotates its own transform.
- `openAngle = 90` rotates local +x toward world −z. Work out which way that swings *before* placing the hinge so the door doesn't sweep through furniture or a wall.
- Set `player` to the `Player` transform in the same script; the door does nothing without it.

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

`PlayerInteractor` on the Player raycasts from the camera, picks **one** target per frame, lists what
can be done to it, and dispatches the key. **Never** add proximity checks or `Input.GetKeyDown` to a
new object — implement `IInteractable` instead, or two things will fire at once.

**Key map (the interactable declares its own key, so nothing can silently steal one):**

| Key | Action | Declared by |
|---|---|---|
| **E** | open / close doors | `DoorInteraction.useKey` |
| **F** | pick up an item, and pour fuel in | `Carryable.pickUpKey`, `Generator.refuelKey` |
| **Q** | drop what you are holding | `PlayerInteractor.dropKey` |
| **T** | start / stop the generator | `Generator.powerKey` |

- `GetOptions` fills a list, so one object can offer several actions at once — the generator offers
  refuel (F) and start (T) together, so you never have to put the can down to switch it on.
- Add nothing to the list to refuse interaction entirely.
- Ask what the player is holding with `interactor.GetCarried<T>()`; that is how the generator knows
  whether to offer refuel at all.
- Carryables (`Carryable`, `FuelCan`) disable their colliders while held so they neither shove the
  player nor block the interaction ray. Drop with G.
- Anything interactable needs a collider, and colliders must be enabled to be looked at — doors are
  deliberately unhittable mid-swing for this reason.

### Economy as of now

Generator tank 100. A full night costs **75** fuel. A can holds **40**. So one can buys roughly half
a night, and a full tank plus one can does not quite cover two nights — refuelling is a recurring
trip, not a one-off errand. Retune these together, never one alone.

### Monsters

`MonsterSpawner` on `Systems` spawns a wave at **dusk** and clears it at **dawn** — "they don't come
during the day" is true in the simulation, not just in the journal. `Assets/Prefabs/Monster.prefab`.

- Monsters must **never** decide safety by measuring distance themselves; they call
  `Generator.GetProtector` / `IsPointProtected`. A monster whose destination is protected walks to
  the boundary and waits there. `Monster.Move` also re-checks after every move and pushes back out,
  so no collision slide or steering bug can ever put one inside the light.
- Steering is direct with a `CharacterController` (flat level, slides off walls for free).
  Switch to `NavMeshAgent` when a level has real geometry — `com.unity.ai.navigation` is installed.
- Balance now: chase 4.0 vs player walk 5 / sprint 9 — always outrunnable, so death is a mistake,
  not a dice roll. 25 damage every 1.2s = 4 hits, ~4.8s of standing still.
- `PlayerVitals` regenerates **only inside protection**, which makes the house the only place to
  recover without saying so.
- **Doors:** a hunting monster that meets a shut door leans on it for `doorForceTime` (1.4s) and
  then it swings open. It cannot reach a door while the generator runs — the whole house sits inside
  the radius — so "they can open doors" is really "once the light dies". `DoorInteraction.canBeForced`
  can be cleared for a door that should hold.
- **Known limit:** steering is direct, so monsters navigate open ground and doorways but snag on
  interior corners. Bake a NavMesh and swap to `NavMeshAgent` before relying on indoor pursuit.

### On-screen text

All placeholder HUD goes through `Hud` (`Assets/Scripts/Hud.cs`) — never raw `GUI.Label`, whose
12px dark-grey default is unreadable against a night field. `Hud.Row(n, text, tint)` for corner
readouts, `Hud.CentrePrompt(text)` for the interaction prompt. Sizes scale with screen height.

Row numbers are claimed and must not collide: **0-1** TimeOfDay, **2** Generator, **3** PlayerVitals,
**4** MonsterSpawner. Claim the next free number for a new readout.

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
