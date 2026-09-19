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

- Roots: `Directional Light`, `Global Volume`, `Player` (CharacterController + `PlayerMovement`, child `Main Camera` + `MouseLook`), `Systems`, `Generator`, `FuelCans`, `PlayerRespawn`, `Ground`, `Boundary`, `House`, `Forest`, `Mountain`, `Props`, `SellStation`, `Store`, `LootSpawnPoints`.
- **The environment is generated, not hand-placed.** `Assets/Editor/PrototypeEnvironmentBuilder.cs`
  (menu **Lab ▸ Environment ▸ Build Prototype Environment**) rebuilds `Ground`, `Boundary`, `House`,
  `Forest`, `Mountain`, `Props` and `LootSpawnPoints` from scratch out of primitives. Change the level by editing that script and
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

### The map — 200 × 200 of woods, one base, and nothing else

`GroundHalf` is **100**, so the playable square is 200 m a side. Walking centre-to-corner is ~140 m.
**The level is deliberately just forest plus the house and its generator** — there are no landmark
buildings, no points of interest and no authored places to search. What moves between runs is the
four key fragments, and nothing else does.

- **Forest is three bands and the density falls with distance** (`ForestInner` 14 → `ForestNear` 55
  → `ForestMid` 88 → an outer band to `GroundHalf - ForestEdge`). Near woods stay dense because that
  band has to read as a wall from the porch; the mid band is thinner on purpose. **That is a budget
  decision, not a look:** at the near density a 200 m map is thousands of renderers and a NavMesh
  bake to match. If the far woods feel too open, raise the mid count before the density.
- **The outer band and the undergrowth use `ScatterSquare`, not `Scatter`.** Polar scatter can only
  fill a *disc*, which on a square map leaves the four corners as open grass — about a quarter of a
  200 m level, and it reads as the edge of the world. `ScatterSquare` samples the square and rejects
  anything inside the inner radius. Never put the outer band back on `Scatter`.
- **`Trails` are old logging tracks that fork rather than radiate**, drawn as render-only dirt by
  `BuildTrails`. They all **give out somewhere in the trees and nothing is placed at their ends** —
  they are something to navigate by, never a route to anything. A track that ended at a fragment
  would be a marker, which is exactly what this level must not have.
- **Tracks and the gate's ground are reserved before a single tree is placed.** `Blocked` rejects
  anything within `TrailClear` of a track or 7 m of `GatePos`. Growing the forest first and carving
  after leaves trunks standing in the path, because `Scatter` has already committed.
- **Everything is scaled to this map and must be retuned together:** `MonsterSpawner.areaRadius`
  **88** (which also sets each monster's `roamRadius`), `NightDepth` **22/85**,
  `LootSpawner.shallowRadius`/`deepRadius` **22/90**, `Expedition` band **30–92 m**,
  loot rings Yard 9–20, Field 20–45, Deep 45–95.
- **The south-east quarter is not forest — it is the mountain** (below). The woods fill the other
  three corners exactly as before.

### The mountain — the one place with no sky over it

`BuildMountain` in the environment builder. The south-east corner of the map is a mass of rock with
one cave in it: the level's **optional** place, darker and further out than anything else, holding
better loot per piece and the second of the two UV/powder mysteries. Nothing in Level 1's own
progression is inside it, and nothing may be put there — see *Where the fragments can hide*.

- **It is laid out in `(s, t)`, never in `(x, z)`.** `s` runs from the house out along the diagonal
  to the corner, `t` across it. In those coordinates the quarter is just "`s` past
  `MountainFront` (**52**)", and the map's own edges are `|t| <= MountainReach - s`, so the mass
  narrows to a point at the corner with no hand-written boundary numbers anywhere. `Mountain(s, t)`
  converts; `MountainS(p)` goes back.
- **One mass with one hole in it.** The wedge is filled with a grid of interlocking blocks
  (`MountainCell` 6, overlapping by 1.4 so no two ever leave a seam to squeeze through), rising from
  **11 m** at the face to **37 m** at the corner with two long waves over that so the skyline reads
  weathered rather than cut. A block whose centre falls inside a carve from `CaveRuns` is not
  skipped — it is **started at `CaveCeiling` (6.5) instead**, so the box that would have been solid
  rock becomes the roof over a passage. There is deliberately no separate cave shell to keep aligned
  with a separate mountain, and a void can never end up open to the sky.
- **`CaveRuns` is the single description of the cave**: runs of `(s, t, carve)`. The mass, the
  gravel floor, the dark volumes, the NavMesh cut-out and the loot regions are all read off it.
  A carve is measured to a block's *centre*, so the walkable width is the carve less half a block
  less the jitter — **6.6 gives about 4.6 m**. Do not take a passage carve much below 6: the grid
  starts pinching shut on the diagonal and a player one metre wide cannot get through. This was
  measured with a capsule flood-fill, not by eye.
- **The mass is overrun by a block past the map edge.** Stopping exactly on the boundary leaves a
  gap between the last block and the boundary wall wide enough to walk down — round the outside and
  straight into the far end of the cave. It did, the first time.
- **`NavMeshModifier` on the root, `overrideArea` = Not Walkable, applied to children.** The blocks
  are big flat-topped boxes, so without it the bake covers the mountain in walkable islands twenty
  metres up and 14% of monster spawn candidates land on the roof of the level. Separately,
  `MonsterSpawner` now requires a spawn point to sample onto the NavMesh at all — measured 0 of 800
  candidates on the mountain afterwards.
- **The cave interior is carved OFF the NavMesh** by `NavMeshModifierVolume`s under `CaveOffMesh`,
  one per passage. That is a decision, not a side effect: the wave in the woods has no idea the
  place exists, and a blind hunter wandering into a pitch-dark cave is a balance question. **This is
  the seam the mountain's own monster arrives on** — delete those volumes, or give it its own agent
  type, and the cave joins the walkable world in one bake.
- **`MountainInterior` (on the `Mountain` root) is the "no sky here" query.** Boxes scaled to each
  passage; `Weight(point)` is 0 out in the woods and 1 well inside, faded by `softness` (4 m) on the
  horizontal faces only — a cave is a few metres floor to ceiling and fading on height would leave a
  standing player permanently half-lit. `NightDepth` folds it in and stays the only thing in the
  game that writes ambient, fog or the moon. Measured: **1.00 everywhere inside** including all 23
  UV marks, 0.93 at the mouth, **0.00 five metres outside it**.
  - The volumes overrun each leg by `softness` at both ends so consecutive ones overlap by twice it.
    Without that the length fade meets another length fade at every bend and each corner of the
    cave is a patch of half-light — measured at 0.32 where it should be 1.
  - It is on the mountain rather than on `Systems` because it owns no roll and no shared state, only
    an answer about where the rock is. It is rebuilt with the rock.
- `DarkQuarter.fixedCorner` is **SouthEast**, not `Roll`: a mountain cannot move between runs, so
  the dark cannot either. The approach to the rock is the part of the map you need a bought
  flashlight for, and the mouth is something you come upon rather than see from the treeline.
  `Level2Site` is what keeps the UV trail out of that corner now (below). Set it back to `Roll` and
  the original per-run behaviour returns untouched.
- `Blocked` keeps trees `MountainClear` (6 m) off the face and 10 m off the mouth, and the two
  tracks that used to run into this corner now give out well short of the rock — **a track ending at
  the cave mouth would be a signpost to the one thing in the level worth finding on your own.**

#### The mystery inside it

The same two tools as the Level 2 trail, and a second, entirely optional chain: **find the dead
man's note → sweep with the UV lamp → follow the marks to the back of the far gallery → pour powder
at a blank wall → a door → what he was looking for.**

| Piece | Owner | Where |
|---|---|---|
| The account of what he saw | `Readable` | `Mountain/Camp/Camp_Note` |
| Marks only ultraviolet shows | `UVRevealed` (unchanged) | `Mountain/UVTrail`, 23 of them |
| A wall that is not a wall | `Concealed` (`IPowderRevealable`) | `Mountain/HiddenDoor` |
| The rock around it | plain geometry | `Mountain/DoorWall` — a **sibling**, see below |
| The door itself | `DoorInteraction` (**E**), dormant until found | `Mountain/HiddenDoor/Door_Hinge` |
| What is behind it | fuel cache + the run's deepest loot spots | `Mountain/Vault` |

- **The note never names a mechanic.** It is a man's account — "the violet lamp", marks "there while
  the lamp is on them and gone the moment it is not", a wall where "the air moves against my hand",
  and a friend who says "a handful of anything fine enough would settle it. Chalk. Flour. Ash."
  Working out that those are two things the store sells is the player's job, and this note is now
  **the only place in the level that hints at either tool** — it is never phrased as an instruction.
  Nothing gates on it: a player who has already met the Level 2 trail needs no note at all.
- **`Concealed` hides by swapping, not by switching off.** `UVRevealed`'s trick — renderers off —
  cannot work for a door set into rock: switch its renderers off and you are looking down a corridor
  that carries on into the dark, which gives the secret away to anyone who walks up to it; switch
  its colliders off and you walk through the mountain. So while it is concealed what stands there is
  `Door_Plug`, a slab of the same rock as everything around it, solid and flush. The powder swaps
  plug for door, once, and never back. Its colliders are deliberately **not** in the swap.
- **The `DoorWall` is a sibling of the door, not a child of it.** The powder finds what it has
  landed near by looking up the hierarchy from whatever collider it touched, so a fourteen-metre
  wall parented under the door would make every inch of the back of the gallery a place to find it.
  Out on its own the wall is only rock. Measured: the pour works **within 5 m either side of the
  doorway**, and does not work at the camp or the mouth.
- **The wall is fourteen metres wide on purpose.** The far gallery is a carved blob, not a tube, so
  the open floor at that plane is several metres wider than the passage leaving it; a wall spanning
  only the passage leaves floor to walk round its ends and the vault is reachable with no powder at
  all. It was. Verified by flood-fill: **0 of the 5 vault loot spots reachable before the reveal.**
- **The vault is never a guaranteed payday.** It holds two fuel cans from the store prefab — fuel is
  the only thing in this level that is really time — and five loot spots at the highest `extraDepth`
  in the game. Everything else is the ordinary draw. A vault that always held the television would
  turn the whole discovery into a route to run every night, which is exactly what Level 1 was
  rebuilt to stop being.
- `Level2Site` now rejects a bearing three ways: the door's own spot clear (as before), **not into
  the dark corner** (`DarkQuarter.DistanceFromDark`, only asked while `IsFixed` — while the corner
  is *rolled* the dependency runs the other way and must stay that way), and **every mark able to
  reach real ground**, which is one general rule that happens to keep the trail out of the rock.
  Measured: **47% of bearings are clear**, against 40 attempts.

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

### Level 1 — four key fragments, then the way into Level 2

The progression, end to end. Each arrow is an object in the world, not a quest stage:

**find 4 `KeyFragment`s in the woods → fit them into the `KeyMaker` on the kitchen table → it makes
the `CompleteKey` → buy the `UVFlashlight` → find the blood trail leaving the yard → follow it out
into the forest to the pool of blood → scatter `MagicPowder` on it → the `Level2Door` appears →
unlock it with the key → it opens, level complete.**

No markers, no objective list, no timer. The only readout in the whole chain is the key maker's
`n/4`, because that is the one number the player genuinely cannot infer from looking at things.

| Piece | Owner | Where |
|---|---|---|
| Scattering the fragments, completion | `Expedition` (authority, `Instance`, `HasAuthority`) | Systems |
| A piece of the key | `KeyFragment` (a `Carryable`, pack-storable) | `Prefabs/Objective/KeyFragment.prefab`, spawned at run time |
| Turning four pieces into one key | `KeyMaker` (`IInteractable`, **E**) | `House/Furniture/Kitchen/KitchenTable/KeyMaker` |
| The finished key | `CompleteKey` (a `Carryable`) | `Prefabs/Objective/CompleteKey.prefab`, made by the `KeyMaker` and **nowhere else** |
| Seeing ultraviolet | `UVFlashlight : Flashlight` | `Prefabs/Store/UVFlashlight.prefab`, store stock |
| Being invisible until UV hits it | `UVRevealed` (pure observer) | every drop of blood, and the pool |
| Probing for hidden things | `MagicPowder` (a `Carryable`, **left mouse**) + `PowderPatch` | store stock |
| The ending | `Level2Door` (`IInteractable`, **E**) | `Props/Level2Site/Level2Door` |
| Swinging the whole trail onto a new bearing each run | `Level2Site` (authority) | `Props/Level2Site` |
| The ending screen | `LevelCompleteHud` (pure observer) | Systems |
| The one mandatory note | `Readable` on `House/Detail/Notice` | builder / Player |

#### The key maker

- **It offers nothing unless a fragment is in your hands** — the generator-refuel and sell-counter
  rule, so it is furniture until it matters.
- **The count is visible three ways and stored once.** A fitted fragment is pinned into one of four
  sockets and a lamp over it lights; the prompt reads `(3/4)`; `Hud.RowRight(4)` carries the line
  while it is part-full. All three read `KeyMaker.Fitted` — there is no second counter anywhere.
- `fragmentsNeeded` is **wired from `Expedition.fragmentsInLevel` by the builder**, never authored
  twice, so changing how many the level hides cannot leave the device asking for a different number.
- **The fragments are destroyed when the key is made**, not hidden. They have gone into the key and
  nothing should be able to find them again.
- **`CompleteKey` has exactly one source.** It is in no loot table, no store stock and nowhere in
  the scene — `KeyMaker.MakeKey` is the only thing that instantiates it. Keep it that way.

#### The blood trail

Somebody bled their way out of the yard and into the woods, and the pool where they stopped is
where the door is. It is the same `UVRevealed` machinery it always was, re-skinned — 25 `Drip_n`
stations of spatter and smears, then `BloodPool`, all under `Props/Level2Site`.

- **`UVFlashlight` is a `Flashlight` subclass and adds one thing: a registry of lit beams.** Pickup,
  carrying, the **X** key, going dark while stowed — all inherited, none restated.
- **The cone is read from the beam `Light` itself** (its `range` and `spotAngle`), so what is
  revealed is exactly what the player sees lit. Retuning the light retunes the reveal for free and
  the two can never drift apart.
- `UVRevealed` switches renderers and **nothing else** — no objective state, no idea what it is part
  of. That is what lets the same component serve the drops, the pool, the cave's marks, and whatever
  is worth hiding next. `AnyLit` is the static early-out so the no-flashlight case costs one check
  per mark.
- `linger` (0.35 s) stops a mark flickering out the instant the player's aim drifts off it.
- **`Env_Blood` emits, and that is not decoration.** Every UV mark used to be `Env_Paint` — a dull
  grey (0.42, 0.41, 0.37) with no emission, lit only by the violet lamp against near-black ground at
  night, which is to say invisible *even when the reveal was working perfectly*. The reveal was
  never broken; finding a mark simply bought you nothing you could see. Emission is what makes a
  revealed mark read as fluorescing. It is **(5.2, 0.23, 0.40)**, which looks absurd next to
  `Light_WarmBulb`'s 3.2 until you remember these are 6 mm-thick decals on unlit ground — judge it
  from a capture in play, never from the swatch. It costs nothing while hidden, because `UVRevealed`
  switches the *renderer* off and not the material.
- **The trail's bearing is rolled every run.** The builder lays it out along local **+z** from 12 m
  to 90 m; `Level2Site` rotates the root about the house at `Start`, rejecting bearings that would
  bury the door, then ground-snaps every mark. A fixed trail is learned once and walked straight to
  — the exact failure this level was rebuilt to avoid. Measured over 120 bearings after the change:
  **74% are clear** (it was 47%), average 2.9 marks lost of 27, worst case 13.
- **It starts 12 m out — at the edge of the yard, not forty metres into the trees.** Forty was the
  original rule ("you have to already be exploring before there is anything to find") and it did not
  survive playtest: a half-metre-wide line somewhere on a 250 m circle, hunted at night with an 18 m
  lamp, is a lottery and not exploration. **The trail was never the content; following it seventy
  metres into the dark is.** So the lamp is what you buy to pick the trail up outside the front door,
  and the woods are still where it goes. Don't put the start back out at 40.
- **The drops grow and multiply towards the pool** (`t` from `InverseLerp`, 2 → 5 drops a station,
  sizes scaling with it, an occasional `Smear` where they went down). That is the only thing telling
  the player which way along the line is *onward*, and it does it without a marker or a prompt.
- **Size is the whole difference between a findable trail and an invisible one, and it was
  measured in play.** The first version's drops came out **6 cm across** — 0.3° of view at 12 m,
  about five pixels — so a player had to stand within two or three metres of one to see it, on a
  trail whose bearing moves every run. The reveal was working perfectly the entire time; there was
  simply nothing big enough to notice. Now every station carries a mark of **0.51–2.61 m**
  (avg 1.40), measured **2.5°–9.5° of view** with six stations lit at once across 7–22 m. Never
  shrink these back; if the trail ever feels wrong again, measure apparent size in play before
  touching the reveal.
- **In the Scene view the marks are buried, and that is not a bug.** The stations are authored at
  world y = 0.020 and `Ground`'s surface is y = 0.040, so in edit mode they sit *inside* the ground
  cube; the authored y is really "lift above whatever ground I land on", which `SitOnGround` applies
  at run time to put them at y = 0.060. It does mean **the trail cannot be judged from edit mode at
  all** — which is how it shipped twice unverified. Check it in play.
- **The bearing moves every run, so "it was there last time" is never a symptom.** `Level2Site`
  re-rolls it in `Start`; runs measured 330°, 49°, 203°, 266°. A player who walks the same way
  twice will find nothing the second time, and that is the design working, not a fault.
- **`Level2Site.fixedBearing` pins the trail while testing. It is currently set to 195°**, which
  puts the first mark 7.9 m from where the player spawns. Negative restores the roll, which is the
  shipping behaviour — set it back before judging how the level actually plays. A pinned bearing
  still has to pass the same checks as a rolled one (a fixed bearing that buried the door would be
  worse, not better), and a refusal logs and falls through to the roll. Measured over 15° steps:
  **due south, south-east and east are blocked** by the door's own spot and by marks that cannot
  reach ground; north, west and north-west are clear.
- **`Level2Site.logBearing` prints which way it went**, e.g. *"Blood trail runs west from the house
  this run (266°)"*. Same reasoning as `DarkQuarter.logChoice`: a rolled thing that leaves no trace
  is indistinguishable from a broken one, and a tester needs to tell those apart. It is a
  `LogWarning` rather than a `Log` purely so it cannot be scrolled past — clear the flag when the
  level stops being worked on. It is **console only and must stay that way**: putting the bearing
  on the HUD would be the objective marker this level exists without.
- **The marks are nudged clear of trunks, and the door's spot is checked properly.** The bearing is
  rolled *after* the forest exists, so a drop can come down inside a tree. `SitOnGround` accepts a
  spot only when the ray lands on `Ground` itself, and otherwise steps the mark round a small spiral
  of `nudgeRadius`.
- **`lostMarksAllowed` is 5, wired by the builder**, not the field's default of 2. The trail is
  nearly twice as long as it was and its first few metres cross the yard, where the shed, the wreck
  and the lamps stand, so losing three or four drops to a prop is ordinary now rather than a sign
  the bearing points into rock. With drops this dense a handful of gaps is not a gap in the thread.
- **`DoorSpotClear` filters the site's own colliders explicitly** rather than trusting that
  `Level2Door.Awake` has already switched them off. That ordering does hold (every `Awake` precedes
  every `Start`), but a check that silently depends on execution order is a trap — and this one
  would fail *closed*, rejecting every bearing in the level and falling back to the authored one
  without anyone noticing.
- **The UV lamp was widened to match** (`Prefabs/Store/UVFlashlight.prefab`): range 18 → **24 m**,
  spot 26° → **32°** (inner 24°), intensity 45 → **110 lm**. At 18 m / 26° a sweep lit a disc barely
  8 m across at its far end. The lumens go up with the cone and the range for the reason in *The
  store* — a wider beam is a dimmer one unless they do. It is still shorter and narrower than the
  plain flashlight (32 m / 42°), so it stays the specialist tool rather than an upgrade.
- **The cave's marks are the same blood**, for continuity and for the same visibility reason — a
  grey sole under a violet lamp in a pitch-dark cave was invisible too.

#### The door

- **Three states, never backwards:** hidden (renderers *and colliders* off, so there is not even an
  invisible wall to walk into) → revealed by the powder → unlocked by the key, which swings it and
  fires `Expedition.CompleteLevel`.
- **The jar never runs out.** It is bought once and kept — a permanent tool, not a consumable.
  There are no charges, no durability and no cooldown on *using* it. The powder's job is to be
  asked the same question over and over while the player sweeps a hillside, and anything that made
  each pour cost something would push them back to standing still and guessing.
- **Pouring is a real gesture, not an instant effect.** Left mouse tips the jar over `pourTilt`
  (**115°**) across `pourSeconds` (**0.42 s**) and rights it again — one float shaped into an
  out-and-back sine, like `DamageFeedback`'s flash, so no coroutine is left running and a second
  click can only be ignored rather than leaving two motions fighting over the transform.
- **115° is past horizontal on purpose.** The jar's mouth is its local **+Y**, so at 78° the mouth
  still points *forward* and the jar reads as being presented rather than poured; past 90° it
  points down and away, over the ground being aimed at. Measured through the arc:
  upright → (0, −0.42, 0.91) at the peak → upright.
- **The dust lands at `pourDelay` (0.13 s), not on the click**, so it appears *because* the jar
  tipped rather than a moment before it. The spot is pinned at the click, so it falls where the
  player was looking even if they have turned away by the time the jar finishes.
- The motion is applied **relative to the authored held pose** (`heldEuler`/`heldPosition`), never
  absolutely — the carry socket is deliberately cocked and an absolute pose would fight it.
- **Every route out of the hands cancels it** (`OnPickedUp`/`OnStowed`/`OnDropped`, plus a guard in
  `Update`). A stowed object's `Update` does not run, so without those a jar put away mid-tip would
  resume its pour when taken back out.
- Ignoring a click while the jar is already tipping is the **motion finishing, not a limit on how
  often the powder may be used**. There is no such limit.
- **The powder asks for `IPowderRevealable`, not for a `RevealCircle`.** It pours, looks for anything
  within reach that offers itself up through that interface, and tells it — so the mountain's hidden
  door needed no change to the jar at all, and the next hidden thing will not either. Whatever
  implements it needs a collider the overlap search can hit **while the thing is still hidden**: a
  trigger volume is the usual answer (the pool's `Pool_Volume`, `Concealed.probeVolume`).
- **`RevealCircle` is found, not used — it is not an `IInteractable` at all.** There is no prompt
  and no key on it, because a pool that announced itself would give the door away to anyone who
  walked past without a UV flashlight. The pour does an `OverlapSphere` and asks whatever is in
  reach whether it wants revealing, so the next hidden thing only has to offer itself the same way.
  It still carries its old name: it is the *component*, and what it sits on is now the blood pool.
- Its volume is a **trigger**: a solid box would be an invisible wall in the middle of the woods,
  and the pour's overlap passes `QueryTriggerInteraction.Collide`.
- `revealRadius` (**3.5 m**) is deliberately wider than the pool. Hunting a 3 m pool with a 3 m
  probe would be miserable; the blood trail is what narrows the search, not the powder.
- **Left mouse is routed, never read with `Input`.** `MagicPowder` declares it from `GetOptions`
  while `IsHeld`, exactly as `Flashlight` declares **X**, so the one-router rule holds.
- **It stands down while the store is open** (`Store.IsAnyOpen`). The store is the only place in the
  game where the mouse means something other than the world, and without this every click on a Buy
  button would also tip the jar out on the shop floor.
- Pouring emits its own **6 m `Noise`** straight to the bus. Keys routed to a held item are silent
  by rule, but that rule is for switches — tipping a jar out on the ground is a world action, the
  same reasoning the store's purchase noise follows.
- `PowderPatch` is **pure presentation**: the dust and one line of text ("The dust settles. Nothing
  here." / "The dust catches on something that is not there."). Patches are left lying where they
  fall on purpose — a player sweeping a hillside needs to see where they have already looked.
- **The whole site carries `NavMeshModifier.ignoreFromBuild`.** At bake time it is still on its
  authored bearing with the door's colliders enabled (`Awake` has not run in edit mode), so without
  this the bake would carve a hole exactly where the door *isn't* once the level starts.
- The door offers the unlock **only while the key is in your hands**, the sell-counter rule. It
  deliberately shows no "it's locked" prompt to an empty-handed player: by the time the door can be
  found at all, the key has already been made.

#### What was removed, and why it must not come back

The level used to be "find Tom's case at his camp and open it at the kitchen table", then "assemble
Tom's torn map", then briefly "fit the fragments into a gate". All are gone, along with `TomsCase`,
`CaseTable`, `MapPiece`, `MapHud`, `Cache`, `Gate`, the nine POI buildings, Tom's camp and the paint
trail. The reason the first version failed in playtest is worth keeping: *the objective was in one
fixed place, so the player already knew the answer and had no reason to explore.* Anything that
reintroduces a fixed objective location — an authored hiding place, a landmark that always holds a
piece, a track that leads to one — brings that failure straight back.

What deliberately stayed: the forest and its tracks, the house, the generator, the shed, the well,
the wreck and the notice by the door.

- **Nina's drawing in the shed is gone.** It used to be the second place the two tools were
  hinted at, pinned inside the shed behind the house. The cave camp's note now says the same
  things in the same indirect way, and one sheet of paper behind the base saying what a page in
  the mountain already says is a duplicate, not a second chance. `Mountain/Camp/Camp_Note` is the
  only hint at either tool. Don't put another readable by the house.

- **The tracks lead nowhere on purpose.** They are something to navigate by, never a route to
  anything; nothing is placed at their ends. A track that ended at a fragment would be a marker.

#### Where the fragments can hide

Three rules shape the draw, and each one exists to keep searching honest:

- **Outside `minDistanceFromBase` (30 m from the generator)** — so no piece is ever collectable from
  inside the protection, and none can land in the house. This is the rule the user asked for
  directly: never at the base, always somewhere else.
- **Inside `maxDistanceFromBase` (92 m)** — so nothing ends up jammed against the boundary wall.
- **`minSeparation` (35 m) apart** — so the four are spread around the compass and finding one never
  means you have nearly found the next.
- **On real, walkable ground.** `Expedition.Ground` raycasts down, rejects faces steeper than
  `maxSurfaceSlope`, then overlap-boxes above the hit. **The box is what catches a point inside a
  trunk** — a downward ray that *starts* inside a collider passes straight through it and finds the
  floor beneath, so the ray alone would happily bury a fragment in a tree. Same reasoning as
  `LootSpawner.TryResolveSurface`.
- **Never *on* the mountain — but one piece is now deliberately *inside* it.** `fragmentsInCave`
  is **1**, so the cave is on the critical path: the level cannot be finished without going under
  the rock, with the light that needs. **This reverses an earlier decision knowingly** — the cave
  used to be refused outright precisely so the level's optional place stayed optional. The other
  three still keep out, so three of the four are always findable without going in.
  - The top of the rock is still refused for everything (`mountainRootName`): it is flat, passes
    every other test, and is somewhere no player can stand.
  - `Ground` takes a `wantCave` flag and the cave test is **inverted rather than dropped**
    (`underRock != wantCave`), so "inside the cave" and "outside it" stay one rule with one
    definition and cannot drift apart.
  - **The probe's ray starts somewhere different in the cave, and that is the whole trick.** Out
    in the woods it drops from `probeHeight` above; inside the cave that is *above the mountain*,
    so it lands on the roof under 11–37 m of rock. Measured before the fix: **1990 of 2000 cave
    samples hit the roof**, and the cave silently never got a fragment. In the cave the ray
    starts at the sample point, which is already inside the passage.
  - **`MountainInterior`'s volumes are sampled at ±0.32, not their full footprint.** They overrun
    each passage by `softness` at both ends so consecutive ones overlap, which means their outer
    edges are buried in stone. The first working version put a fragment exactly there: 99 m of
    headroom (open sky, not cave at all) and no room for a player to stand.
  - **`StandableInCave` is what makes it fair**: weight ≥ 0.95 (well inside, not in the fade),
    rock overhead within 12 m and at least 1.9 m of it, and a player-sized capsule that fits. A
    piece visible down a crack and unreachable is worse than one that is not there.
  - A volume is drawn in proportion to its **floor area**, or the little chambers would see as
    many pieces as the long galleries.
  - Measured after all of it: **82.7% of cave samples are usable** (1,654 of 2,000 — 226 refused
    by the capsule, 97 by the fade, 13 as open sky, 10 as rock), so placement cannot realistically
    fail. It logs and falls back to the woods if it ever does, so a level built without a mountain
    still puts all four out.
- A fragment is pack-storable, so it costs one of the four slots — "carry this back or carry the
  radio back" is a decision made in the field, not in a menu.

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
  a door is forgiving across its whole face, a dropped flashlight still gets a real area.
- A candidate that returns no options must not block the ones behind it — that is why the search
  walks a sorted list instead of committing to the single best score. An empty-handed player at the
  sell counter still gets the door prompt.
- Line of sight is rechecked per candidate (`HasLineOfSight`), which is the only thing stopping
  interaction through walls. The ray stops 2 cm short so an object cannot occlude itself.
- `aimTolerance` under 90° is what excludes things beside and behind the player. Don't raise it past
  that; there is a `[Range(1, 85)]` on the field for this reason.
- Measured with the defaults (range 3.5, tolerance 32°): doors ~±50° of yaw, the sell counter ~±56°,
  a flashlight on the floor a 42°-tall by 114°-wide window. Near a door, the flashlight needs a deliberate look
  down — the door legitimately wins at head height.

**Key map (the interactable declares its own key, so nothing can silently steal one):**

| Key | Action | Declared by |
|---|---|---|
| **E** | open / close doors, sell at the counter, open / close the store, revive a body, read a note, fit a key fragment into the key maker, **and** unlock the Level 2 door | `DoorInteraction.useKey`, `SellStation.sellKey`, `Store.browseKey`, `PlayerBody.reviveKey`, `Readable.readKey`, `KeyMaker.fitKey`, `Level2Door.unlockKey` |
| **F** | pick up an item, and pour fuel in | `Carryable.pickUpKey`, `Generator.refuelKey` |
| **Q** | drop what you are holding | `PlayerInteractor.dropKey` |
| **Left mouse** | pour a little magic powder where you are looking | `MagicPowder.pourKey` — routed to the *held* item, like the flashlight's X |
| **R** | tip a dead player's belongings out of their body | `PlayerBody.searchKey` |
| **T** | start / stop the generator | `Generator.powerKey` |
| **X** | switch the flashlight on / off, UV flashlight included | `Flashlight.toggleKey` (inherited by `UVFlashlight`) |
| **1–4** | select a pack slot | `PlayerInventory.slotKeys` — **not** routed; see below |
| **C** | crouch toggle | `PlayerMovement.crouchKey` — **not** routed, like 1–4 |

`PlayerInventory` reads 1–4 itself, and that is not a violation of the one-router rule: a slot is
not something you aim at, so it is a player system like `PlayerMovement`, not an interactable. They
are in this table so nothing else ever claims them — an interactable that declared `Alpha1` would
fire alongside a slot swap. `PlayerMovement` reads **C** (and Shift / Space) on the same footing.

- `GetOptions` fills a list, so one object can offer several actions at once — the generator offers
  refuel (F) and start (T) together, so you never have to put the can down to switch it on.
- **Every interaction makes a noise**, emitted once by the router (`interactNoiseRadius`, 12m) so a
  new interactable is audible to the blind monster without doing anything. Keys routed to a *held*
  item are silent on purpose. It goes out through the player's `NoiseEmitter`, so a **crouched**
  player handles things silently too (see *Crouching* below).
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
  routes those — you switch a flashlight on without looking at it. Declare them from `GetOptions` while
  `IsHeld`, exactly as `Flashlight` does; never add an `Input` call. If the held item and the aimed
  target share a key the **target wins**, so one press can never fire two actions.
- `CarrySocket` is deliberately cocked (0, 340, 8) so a held prop sits at a jaunty angle. Anything
  directional — a flashlight beam, later a camera — must cancel that out via its own `heldEuler`, or it
  will point 20° off the crosshair.
- Anything interactable needs a collider, and colliders must be enabled to be looked at — doors are
  deliberately unhittable mid-swing for this reason.

### Crouching — the quiet walk, not the silent one

`PlayerMovement` owns it; **C** toggles (not hold). Tuning on the Player: `crouchSpeed` 2.0,
`crouchHeight` 1.2 (the eye drops 0.8 with the feet fixed), `eyeMoveSpeed` 5.

- **A crouched body is very quiet, never inaudible.** It used to emit nothing at all, which made
  crouching an off switch for the whole monster rather than a stealth option; that was the single
  biggest balance problem in Level 1. A crouched step now carries `NoiseEmitter.crouchNoiseRadius`
  (**2.5 m**) every `crouchStepInterval` (**0.85 s**) — something has to be nearly on top of you to
  hear it, and creeping past a monster is a real gamble rather than a free pass.
- **Standing still while crouched is still perfectly silent**, and that is the player's true zero.
  It is free: a standing body is under `walkThreshold` (0.6), so nothing is emitted. "Stop and hold"
  is a better verb than "hold C", so don't replace this with a special case.
- **One-off noises are muffled, not dropped:** `NoiseEmitter.Emit` scales the radius by
  `crouchNoiseScale` (**0.31**), so the interactor's 12 m becomes under 4. Working a door open beside
  something still costs you a little. The noise still exists, so `Noise.OnNoiseEmitted` and the debug
  overlays see it — never go back to returning early, which hid crouched actions from the overlays too.
- `Emit` is still the one gate for everything the body does, and the interactor emits through it, so
  nothing can bypass the muffle. `IsMuffled` (was `IsSilenced`) reads `PlayerMovement.IsCrouching`.
  Footsteps go straight to `Noise.Emit` because the crouched step radius is *already* the crouched
  one — passing it through `Emit` would scale it twice.
- Noises made by **world objects** are untouched by crouching: the
  store's 8 m purchase, a door a monster forces. Crouching muffles you, not what you disturb.
- The monster has no vision to keep: crouching changes nothing it does except what it hears, and it
  still hurts a crouched player by touch.
- Crouched: no sprint, no jump. Speed composes: whatever is carried slows the crouch by the same
  fraction it slows walking, so the television crouched is 2.8 × 2.0/3.5 = 1.6.
- Standing needs headroom (`HasHeadroom`, an overlap capsule above the crouched head). Pressing C
  under something low queues the stand and it happens once there is room; `Hud.Row(0)` says
  `Crouching - no room to stand` meanwhile.
- The capsule and the avatar's `CapsuleCollider` shrink with the feet fixed. The eye is lowered by
  the *difference* each frame and `CameraShake` offsets relatively, so the two compose.
- Crouch survives the store (which disables movement) and death; it is the player's toggle.

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
  `Carryable.ApplyLargeLoad`, so the television is automatically hands-only —
  retune that with the Large movement numbers, not separately. `Custom` leaves it as authored.
- `PlayerInteractor.PickUpRefusalReason` distinguishes the two refusals ("hands and pack full" vs
  "put the old CRT television down first") because they need different actions from the player.
  `Carryable.PickUpRefusal` puts it on the prompt for every item for free.
- A stowed flashlight goes dark and lights again when taken out, because stowing deactivates the object.
  Override `OnStowed`/`OnUnstowed` for an item that should keep running in the pack.
- **Dying empties the pack as well as the hands, onto the body.** `PlayerInteractor.ReleaseBelongings`
  is the one route: it takes the held item (through `ConsumeCarried`, so the load clears) and then
  `PlayerInventory.ReleaseAll`, and hands the lot to the `PlayerBody`. Nothing is dropped, cloned or
  destroyed on that path — the slots' items are already stowed, so they simply change owner. With no
  `Revival` in the level, `DropCarriedOnDeath` still drops the hand item on the floor and slots
  survive, which is the behaviour that came first.
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

**A spawn point must also sample onto the NavMesh.** The ray finds the first surface under the sky,
which since the mountain means the roof of the level — measured, 14% of candidates were landing on
top of the rock with nowhere to walk. The test is skipped entirely when nothing is baked, so a scene
with no NavMesh still gets its wave, exactly as `MonsterPatrol` still gets its circle. Measured
afterwards: **0 of 800 candidates on the mountain.** The cave is off the mesh on purpose (see *The
mountain*), so the current wave cannot get into it either — that is the seam its own monster
arrives on.

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
| `Investigate` | Stands still at one spot and sweeps its look around for `investigateDuration` (3 s) | timer out → `Search` while `searchesLeft`, else give up; new noise → `Alerted`/`Chase` |
| `Search` | Walks to one place near the noise at `alertSpeed`, then listens there | arrives (or the leg is lost) → `Investigate` |
| `Chase` | Same as `Alerted` but `chaseSpeed`, re-targeting the newest noise at most every `retargetCooldown` | `agitation` decays below `chaseThreshold` → `Alerted` |
| `Prowl` | Walks to a spot inside the house at `patrolSpeed` and has a look round for `prowlLinger` | arrived and lingered, the leg is lost, or the generator starts → `Patrol`; any noise → `Alerted`/`Chase` |
| `Leaving` | Standing in a running generator's light: walks itself out to the boundary at `alertSpeed` | it is out of the light → back to whatever it was doing |

- **Arriving at a noise starts a search, not a stare.** It picks `minSearchPoints`–`maxSearchPoints`
  (**2–4**) walkable points within `searchRadius` (**10 m**) of where it thought the sound came from
  and works through them, listening at each. So the whole sweep is ~20–25 s of it moving around the
  area, and the player's question is "where is it going to look next", not "has the 6 s timer run out".
- **A failed search costs the player the area, not six seconds.** `GiveUpSearch` calms the agitation
  but calls `MonsterPatrol.Bias(searchCentre, searchInterestRadius 18, searchInterestTime 38)`, which
  holds the round near the place that went quiet and **widens the leash back out to the whole map over
  those 38 s**. Nothing has to remember to switch it off, and a second noise simply takes over.
- **Every 10–300 s each monster goes and tries the house, whether or not it has heard
  anything.** `minProwlInterval`/`maxProwlInterval` are rolled per monster and re-rolled after
  every attempt, so the wave never arrives together and no two nights are the same. This is
  **not detection** — it still has no idea whether anyone is in there. It is what makes keeping
  the generator lit worth doing *before* something is already after you, rather than only a way
  out of a chase.
- **The prowl is refused outright while the generator runs**, and nothing in `Monster` says so:
  it asks `MonsterPatrol.ChooseNear` for a walkable spot within `prowlRadius` (5 m) of the house
  centre, and `ChooseNear` already refuses anywhere inside the light. A refusal simply re-rolls
  the timer. If the light comes on mid-walk, the prowl is abandoned.
- A prowling monster **forces doors**, like a hunting or searching one — a shut front door is the
  whole of "trying to get in". It walks at `patrolSpeed`, so the warning is the footsteps and the
  door, never a lunge. The house is found from `Monster.house`, else the `House` root, else the
  origin.
- **A light that comes on around a monster is walked out of, never teleported out of.**
  `TickLeaving` takes over the frame — it heads for `BoundaryPoint` on the NavMesh, forcing a door
  on the way if it was shut in, and steers straight at the nearest edge if there is no route.
  Noises heard on the way out do not interrupt it; they decide the state it leaves in.
  `leaveGraceSeconds` (**8 s**) is the backstop that still places it on the boundary if it truly
  cannot get out, so "never inside the light" holds — but it is the backstop, not the behaviour.
  `Monster.Move` no longer corrects position at all.
- **`agitation` is the aggression dial:** each heard noise adds
  `agitationPerSound * NoiseWeight(radius) * (0.5 + clarity)`, it decays at `agitationDecayPerSecond`,
  and crossing `chaseThreshold` (**4**) turns walking into running. `maxAgitation` (**6**) is headroom
  so a chase survives a few seconds of silence instead of dropping to a walk the instant you stop.
- **`NoiseWeight` is what makes the three gaits three different events**, rather than one event at
  three ranges: `(radius / referenceNoiseRadius) ^ noiseWeightExponent`, capped at `maxNoiseWeight`
  (8 m / **1.5** / 3). Measured at middling clarity, with decay accounted for:

  | noise | radius | weight | outcome |
  |---|---|---|---|
  | crouched step | 2.5 | 0.18 | heard only within 2.5 m, and **decays faster than it accrues — creeping can never reach a chase**, only bring it over to look |
  | crouched interaction | 3.7 | 0.31 | a small, local risk |
  | walking step | 8 | 1.0 | **investigated**: ~5 s of walking inside its earshot to tip into a chase, longer at the edge |
  | interaction | 12 | 1.84 | one press is most of the way to a chase |
  | landing a jump | 14 | 2.3 | nearly a chase on its own |
  | sprinting step | 20 | 3 (capped) | **chase in ~2 steps**, even heard at the far edge |

- **`retargetCooldown` (0.4 s) makes it commit.** While already pursuing it will not swing onto a
  newer noise more often than that — louder noises still stack agitation, it just stops twitching at
  every footfall, which reads as smarter for less code.
- **A searching monster forces doors too**, not only a pursuing one: the NavMesh is baked as though
  the doors were open, so a search leg through a shut one would otherwise wedge against it. Search
  legs carry the same lost-leg guard the patrol legs do (`follower.Blocked`, or longer than
  `patrolSecondsPerMetre` allows).
- Monsters must **never** decide safety by measuring distance themselves; they call
  `Generator.GetProtector` / `IsPointProtected`. `KeepOutOfSafeZone` runs on every heard position
  *and* every destination, so a noise made inside the light draws it to the boundary and no further.
  Anything that still ends a frame inside the light — the generator started around it, a collision
  slide — is picked up by `TickLeaving` the next frame and **walks** out (see above). `Monster.Move`
  deliberately corrects nothing itself.
- **Moving is still the monster's own job; only the route is the NavMesh's.** `NavPathFollower`
  (a plain class, one per monster) turns "go there" into "steer at this corner", and `Monster.Move`
  still drives the `CharacterController` — so gravity, the safe-zone push-out and attack-by-touch
  all stay in one place. There is deliberately **no `NavMeshAgent`**: an agent would own the
  transform and fight all three. Used by pursuit as well as patrol, so chasing rounds corners too.
  `NavPathFollower.Blocked` is the "you cannot get there from here" signal — it is true only when
  both ends sampled onto the mesh and the route still came back incomplete.
- The wave is deliberately still **4** monsters. Better search behaviour was tried on its own first,
  so that the count is a separate decision from the behaviour and neither hides the other.
- Balance now: chase **4.0** vs player walk **3.5** / sprint **6.0** on stamina (see *Sprint stamina*).
  Walking loses 0.5 m/s to a chasing monster; a full sprint buys ~10 m, and sprinting in bursts can
  never average faster than it. Escape is sound — get past its hearing, then walk or crouch — not
  legs. **There is no health: its touch kills outright** (`attackRange` 1.7 m), so being caught
  is never a fight you can survive — `attackInterval` (1.2 s) is only there so one lunge cannot
  take a whole co-op team at once. A player in a running generator's light is still untouchable.

### Sprint stamina

`PlayerMovement` owns it (per player, not static). Tuning on the Player: `maxStamina` 100,
`sprintStaminaDrain` 20/s (**5 s** of sprint), `staminaRegenRate` 5/s after `staminaRegenDelay`
1.5 s (**~21.5 s** empty to full), `exhaustedResumeStamina` 25.

- **The invariant:** `staminaRegenRate ≤ drain × (chaseSpeed − speed) / (sprintSpeed − chaseSpeed)`.
  Long-run burst-sprint speed is (sprint + walk·r)/(1 + r) with r = drain/regen; at the defaults that
  is exactly 4.0 before the delay, and under it with the delay. Retune walk, sprint, chase and regen
  together or the player can outrun the monster forever.
- Drains only while actually sprinting (key held, moving, allowed). Regen waits `staminaRegenDelay`
  after the **last sprinting frame**, so tapping shift restarts the wait and earns nothing back.
- Running dry sets `IsExhausted`: no sprint until stamina is back to `exhaustedResumeStamina`, so a
  held key cannot stutter on scraps. Walking is unaffected.
- A heavy load or a crouch swallows the key before stamina is consulted, so neither spends any.
- `Hud.Row(1)` shows the percentage while it is below full, and `out of breath` while exhausted.
- `showDebug` (on by default) draws the hearing radius, the destination in the state's colour, the
  last heard position and a state/agitation label in the Scene view. `MonsterDebugHud` on `Systems`
  is the in-game readout. Both are pure observers — turning either off changes no behaviour.
- **Doors:** a hunting monster that meets a shut door leans on it for `doorForceTime` (1.4s) and
  then it swings open. It cannot reach a door while the generator runs — the whole house sits inside
  the radius — so "they can open doors" is really "once the light dies". `DoorInteraction.canBeForced`
  can be cleared for a door that should hold.
- **Known limit:** sound is not occluded. A noise through a wall carries as far as one in the open.
- **Known limit:** only a *hunting or searching* monster forces doors, so a patrolling one that routes
  through a shut doorway walks into it, times out and picks somewhere else. Correct, but it costs a leg.

### MonsterPatrol — where it goes when nothing has its attention

`Assets/Scripts/MonsterPatrol.cs` is a component on the monster and **knows nothing about
detection** — not hearing, not sight, not agitation, not the player. Hand it a position, get back a
walkable point. **`Bias(centre, radius, seconds)` is the only thing detection may say to it**, and it
is deliberately vague — "keep your round near here for a while", with no mention of why or of a
player — so the ignorance survives. `ChooseNear(centre, radius, from, out point)` is the other
addition: a pure query for one walkable, reachable point near somewhere, used for search steps and
for picking the spot a prowl heads for. It
does not touch the patrol's own leg, so a monster can search and still have a round to go back to.
That ignorance is the point: a sighted monster can drop the same component on and
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
  destination every frame in case the generator started mid-leg. `ChooseNear` refuses the light too,
  so a search never works its way into the safe zone.
- **A bias is a fading preference, not a cage.** `AreaCentre` / `AreaRadius` lerp from the leash back
  to `areaCenter` / `areaRadius` as the timer runs down, and `MinTravel` follows the radius down so a
  leg still fits inside a small leash. Read those three properties, never the raw fields, or a biased
  monster will wander straight out of the area it is supposed to be haunting — anchor candidates are
  clamped for the same reason.
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
- **The `Mountain` root carries `NavMeshModifier` with `overrideArea` = Not Walkable and
  `applyToChildren`**, so the rock is an obstacle and never a floor — otherwise its flat-topped
  blocks bake into walkable islands twenty metres up. Separately, `Mountain/CaveOffMesh` holds one
  `NavMeshModifierVolume` per passage, also Not Walkable, which is what keeps the current wave out
  of the cave: the cave's floor is the `Ground` cube, so the root modifier does not cover it. Two
  mechanisms, two distinct jobs; don't merge them.
- **`Lab ▸ Environment ▸ Build Prototype Environment` re-bakes at the end** (`RebakeNavMesh`), so a
  rebuilt forest does not leave monsters routing around trees that are gone. `House` is *not* in the
  `Keep` set, so the doors' modifiers are re-added by `PrototypeEnvironmentBuilder.Door`.
- Anything else that changes level collision needs a re-bake. Verify with coverage and a
  `CalculatePath` probe (the whole 200×200 ground samples, and every house room is `PathComplete`
  from outside) rather than by eye.

### Sound — how anything gets noticed

`Assets/Scripts/NoiseEvent.cs` holds the whole system: a `NoiseEvent` struct (position, radius,
source, time), an `INoiseListener` interface, and the static `Noise` bus.

- **Making a sound is always one line:** `Noise.Emit(position, radius, gameObject)`. `radius` *is*
  the loudness — how far it carries in metres. Nothing polls; listeners register in `OnEnable`.
- A listener hears it when `distance <= radius * hearingSensitivity` **and** `distance <=
  hearingRadius`. The position it is handed is blurred by `positionError * (1 - clarity)` (**5 m** at
  the very edge of a sound's carry), so a faint noise is a direction and a close one is a fix. A
  monster ignores its own `source`. **The radius is also how hard it reacts**, not only how far it
  carries — see `Monster.NoiseWeight` — so a new noise gets a sensible reaction for free, and "make
  it scarier" is one number rather than a new field.
- **Who emits today:** `NoiseEmitter` on the Player (footsteps from `CharacterController.velocity`,
  walk 8m / sprint 20m / **crouch 2.5m** / landing 14m — standing still emits nothing at all, in any
  posture), `PlayerInteractor` (12m, once for *every* interaction, so a new interactable is
  audible the day it is written — sent through the `NoiseEmitter`, so crouching muffles it to 3.7), and
  `DoorInteraction.SetOpen` (14m, the code path a monster forcing a door uses; the player's own
  door noise comes from the interactor, so opening one by hand never sounds twice).
- Keys routed to a **held** item are deliberately silent — flicking the flashlight on must not give you
  away, and the flashlight has no effect on a blind monster in any other way either.
- `Noise.OnNoiseEmitted` fires for every noise whether heard or not. Debug overlays only.

### Damage feedback

`DamageFeedback` on the Player and `CameraShake` on `Main Camera` are what make being killed
*readable*. Both are **pure observers**: `DamageFeedback` subscribes to `PlayerVitals.OnDied` and
decides nothing, so deleting it costs the feel of the moment and never the death. There is still
exactly one place a player's condition lives — `PlayerVitals` — and one readout, `Hud.Row(3)`.

- **No coroutines.** The flash is a single float ticked down in `Update`, so a burst of calls can
  only ever *retrigger* the effect: the new flash starts at the brighter of the two and the timer
  restarts. Nothing stacks, nothing is left running. Do the same for anything similar.
- The flash is a **vignette**, not a wash — red at the edges, ~7% opacity dead centre at its
  brightest — drawn in `OnGUI` (this project has no Canvas). The texture is built once and
  rebuilt only when `flashEdgeBias` changes.
- `CameraShake` offsets the camera's **local position** and rolls it around its own **forward
  axis**. Roll cannot change `forward`, so the interaction ray, the flashlight beam and player
  movement are all untouched — never make it yaw or pitch instead. MouseLook rewrites
  `localRotation` every `Update`, so the roll is applied in `LateUpdate` and last frame's roll is
  removed first; it can never accumulate, even with MouseLook off. The positional sway is relative
  the same way (last offset off, new one on), because crouching also moves the camera's local
  position — never go back to writing an absolute rest position.
- `Shake()` is public and generic — a future fall, explosion or slammed door should call it
  rather than grow a second copy of the maths.
- **The hit sound is deliberately unassigned.** `hitSound` is an empty `AudioClip` slot on the
  Player's `AudioSource` (2D, `playOnAwake` off); there is no placeholder. Drop a clip in and it
  plays, pitch-varied so repeated hits don't machine-gun.
- **Nothing scales it any more.** With health gone there is one event and one strength:
  `HandleDied` calls `Play(1f)`. `Play(intensity)` stays public and graded so a future knock — a
  near miss, a falling tree — can use it without inventing a second copy of the maths.

### Death and revive

Death is a **state**, not a teleport. A dead player stays dead until someone brings their body
home and uses **Adrenaline** on it. The pieces are deliberately separate, and each is replaceable:

| Concern | Owner | Where |
|---|---|---|
| Alive / dead, `Kill`, `ReviveAt`, `Respawn` | `PlayerVitals` | Player |
| Controls, avatar colliders and renderers off while dead | `PlayerDeathLock` (pure observer) | Player |
| The body left in the world | `PlayerBody` (a `Carryable`) | `Assets/Prefabs/PlayerBody.prefab`, spawned at run time |
| Revival item | `Adrenaline` (a `Carryable`, `IStorePriced`) | `Assets/Prefabs/Store/Adrenaline.prefab` |
| Revive rules, the revive itself, bodies, the price | `Revival` (authority, `RevivePricing`) | Systems |
| Revives used this run, who is alive, all-dead | `RunState` (authority) | Systems |
| Whether a revive is *safe* to do | `Generator` (`IsPointProtected`) — not `Revival` | Generator |
| Game over when everyone is dead | `RunReset` (authority) | Systems |

- **`PlayerVitals` knows none of the others.** It fires `OnDied` / `OnRevived`, plus the static
  `AnyDied` / `AnyRevived` for run-level listeners, and keeps a static `All` registry of players (a
  scene registry like `Generator`'s, not per-player state). Nothing else kills a player.
- **There is no health, and there must not be one again.** `PlayerVitals` holds one bool and
  `Kill()` is the only way into it — no damage numbers, no regeneration, no bar. A monster that
  reaches you has already won, which is what makes the generator, the crouch and the noise rules
  the whole of the defence. Anything new that can kill calls `Kill()`; anything that would only
  *hurt* does not belong in this game.
- **The body is its own object, not the player's.** So the player object is free to become a
  spectator later, and the body is an ordinary hands-only `Carryable` (television weights:
  `heavyLoad`, so `heavyWalkSpeed`, no sprint, no jump, no pack). Carrying a teammate home is the same pick-up/put-down as anything else; a
  future body teleporter only has to move an un-held body's transform — `Revival` never asks how it got there.
- **`Revival.ReviveRefusal` is the single definition of "allowed"**, read by both the prompt and
  `TryRevive`: body on the ground, nobody already reviving it, rescuer holding `Adrenaline`, rescuer
  alive. **Location is deliberately not one of them.** The body's pickup prompt says which is missing.
- **The counter is global for the run and lives on `RunState`.** It resets in `Awake`, so every level
  load is a new run; `BeginRun()` exists for a new run without a reload. Only `Revival.TryRevive`
  calls `RecordRevive`, and only after a successful revive.
- **Price = `RevivePricing.Price`, a flat $500, every time.** It used to be a curve (500 → 750 →
  1,100 → 1,500, counting unspent doses so a team could not stockpile at the first price), and that
  is gone on purpose: **a price that climbs with every death punishes the run that is already going
  badly**, and it made a second death a run-ender rather than a setback. The question a revive
  should pose is "is it worth $500 and the walk out there", never "can we still afford one". So
  there is now exactly one number on `Revival`, and nothing counts revives for pricing —
  `RunState.RevivesUsed` is still kept, but only as the run's tally. Don't reintroduce an index.
- A revive stands the owner up at the body, whole — there is no health to come back with a
  fraction of. The body and the dose are both destroyed.
- `PlayerDeathLock` re-disables the controls **every frame** while dead, because the store restores
  what it suspended when it closes, and dying with the store open would otherwise hand a corpse its legs back.
- **Everyone dead is game over, and game over reloads the level.** `RunReset` on `Systems` listens
  to `RunState.OnAllPlayersDead`, shows `GAME OVER - restarting in Ns` for `restartDelay` (4 s),
  then `SceneManager.LoadScene`s the active scene. Verified in play: killing the only player took
  it from `allDead=False` to a pending reset, and after the countdown the scene came back with a
  live player on their feet and four freshly scattered fragments.
- **It cannot tell how many players there are, and that is the design.** `RunState` fires the event
  from the death of whoever happens to be the last one standing — the third death of three, the
  only death of one — so there is no player-count branch to drift out of step with co-op.
- **It owns no state to put back, and must never grow any.** Everything a run accumulates — the
  Wallet, `RunState`'s revive count, the generator's fuel, the loot roll, where the fragments are,
  the blood trail's bearing, which corner is dark — is scene state rolled or reset in `Awake` /
  `Start`. So **reloading the scene *is* the reset**, and there is deliberately no "undo the run"
  code for that state to disagree with. It needs the scene in Build Settings (it is, index 0).
- It restores the cursor lock before loading, because dying with the store open is possible and a
  fresh level with a loose cursor and no mouse look is the worst thing to leave behind.
- **Nothing else may answer `OnAllPlayersDead`.** `SingleplayerDeathFallback` still exists in
  `Assets/Scripts/` — it stands the last dead player back up at the house as their own rescuer —
  but it is **not in the scene and must not be added**: a respawn during the countdown would
  cancel the reset through `RunReset`'s revived check, and you would walk back into the house and
  find your own body on the floor.
- One body per player: a new death replaces that player's old body.
- **The body carries what its owner did — hands and all four slots.** `Revival.LeaveBody` calls
  `PlayerBody.TakeBelongings`, which parents each item under the body and `OnStowed`s it: the same
  deactivate a pack slot does, so it is the same GameObject with the same script, value and state,
  and it travels with the body when someone shoulders it. This is why `PlayerVitals.Die` fires
  `AnyDied` **before** `OnDied` — the body must exist before the interactor's own ground drop runs.
- **R searches a body on the ground** (`PlayerBody.searchKey`), tipping everything out in a ring at
  its feet through `Revival.GroundUnder` — the same footing rule the body itself was placed with.
  From there each piece is an ordinary world pickup, so nothing needed a second route into a
  player's hands. A revive spills whatever is left the same way: you come round empty-handed.
- **Nothing about the body's own weight changed:** it was already `heavyLoad`, no sprint, no jump,
  `canBeStoredInInventory` off. So "you cannot carry a body and the television" and "no flashlight in
  hand while carrying a friend" both fall out of the existing Large rules rather than a new check.

#### One revive, everywhere — the generator is the only thing that changes

**There is no such thing as a home revive and a field revive.** `Revival.TryRevive` does not ask
where the body is lying, and there must never be a branch that does: same `reviveSeconds` (**5.5 s**),
same `reviveNoiseRadius` (**25 m**) every `reviveNoiseInterval` (1.5 s), same committed dose, in the
kitchen and forty metres into the woods alike. A faster or quieter revive at home would make the
generator decorative — the safety *is* the reward for hauling a teammate back.

- **What the place decides is only whether that noise can reach you.** Inside a running generator's
  radius the monsters it pulls cannot get in: `Monster.KeepOutOfSafeZone` already runs on every heard
  position, so a revive in the light draws them to the boundary and no further. **Let the fuel run
  out and the house is just another place to kneel down in** — which is the whole point of the
  pressure. Nothing in `Revival` implements that; it falls out of `Generator` as it already stood.
- `Revival.IsProtectedSpot` exists **for the prompt only** (`Generator.IsPointProtected`). It changes
  no timing, no noise and no rule — if it ever gates behaviour, the design above has been broken.
- **Every revive costs one Adrenaline**, at home or out, and always the same $500 — money stays the
  revive sink; the decision is *where to spend the 5.5 s*, never whether to pay.
- **The dose is spent the moment the needle goes in**, before the timer starts. That is what makes
  walking away a loss rather than a free look at a progress bar — and it is why the prompt says so.
- An attempt fails if the rescuer strays past `reviveStayWithin` (3 m), dies, or presses **E** again
  to call it off. `Revival.Update` holds all four failure paths in one place.
- The 25 m noise goes **straight to `Noise.Emit`, not through the rescuer's `NoiseEmitter`** — it is
  the patient and the needle, not the rescuer's body, so crouching over someone does not quieten it.
  At that radius it is `maxNoiseWeight`-capped, and repeating it means a monster in earshot commits
  to a chase rather than losing interest halfway.
- While an attempt runs the body offers **only** cancel: it cannot be shouldered or searched
  mid-injection, so there is no way to walk off with the patient still on the needle.
- `Hud.RowRight(3)` is the progress line, drawn by `Revival` as a pure observer. In co-op it moves
  to the rescuer's own HUD; today there is at most one attempt.
- **`ReviveZone` is gone** — script, meta and the builder call that added one to `House`. It became
  dead the moment location stopped gating revives. Don't reintroduce an "indoors" volume: the
  question a revive cares about is *lit*, and `Generator.IsPointProtected` already answers it.

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
  multiplier, `allowSprint` and `heavy`. Every `Carryable` has one (`Load`), so a fuel can or a future
  crate can be heavy without touching `Valuable`. Default is `CarryLoad.None` — no penalty.
- **`heavy` (`Carryable.heavyLoad`) ignores the walk multiplier** and walks at the carrier's
  `PlayerMovement.heavyWalkSpeed` (2.8, Inspector on the Player), with no sprint and no jump. So every
  heavy thing — television, a body — is retuned in that one field.
- **`PlayerMovement` is pushed the load, it never reads the item.** `SetCarryLoad` / `ClearCarryLoad`
  are the only seam; `PlayerInteractor.ApplyCarryLoad` is the only caller, and *every* route in and
  out of the hands passes through it (`Carry`, `DropCarriedAt`, `ConsumeCarried`), so a penalty can
  never outlive the item that caused it. There is still exactly one movement script.
- **A load that forbids sprinting swallows the sprint key**, rather than scaling it to nothing — so
  holding shift with the television does nothing at all instead of feeling broken. A heavy load
  swallows the jump key the same way. Both come back the frame it leaves the hands.
- `ValuableSize` is the tuning dial and `Valuable.ApplySizePreset` is the one place a size becomes
  numbers (it runs in `Awake` and `OnValidate`, so the Inspector shows what the player will feel).
  Retune a category there and every item in it follows. `Custom` opts an item out and uses the
  multipliers as authored.

| Size | Walk | Sprint | Pack | Items now |
|---|---|---|---|---|
| `Small` | 3.5 (×1) | 6.0 (×1) | yes | old radio $40, camera $60, old clock $75 |
| `Medium` | 3.15 (×0.9) | 5.4 (×0.9) | yes | laptop $150 |
| `Large` | 2.8 (`heavyWalkSpeed`) | **none**, and no jump | **no** | old CRT television $300; a PlayerBody uses the same `Carryable.ApplyLargeLoad` |

- The Large numbers are chosen against the monster: chase speed is **4.0**, so 2.8 means the
  television is the one thing you cannot outrun. That is the risk/reward, not a balance accident —
  retune `chaseSpeed` and `heavyWalkSpeed` together.
- Two things fall out of this for free and should be left alone: `NoiseEmitter` reads the
  controller's *actual* speed, so carrying the television is also **quieter** than running; and
  `PlayerInteractor`'s carrying line says `(heavy)` / `(too heavy to run)` / `(too heavy to run or
  jump)`, because being slow with no explanation reads as a bug.
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
- **Anything bought can be sold back at the `SellStation` for `Store.resaleFraction` (0.5) of what
  was paid.** `TryBuy` stamps a `StoreGood` on the item at purchase carrying its `resaleValue`, and
  `SellStation` sells a `Valuable` for its value or a `StoreGood` for that. It is stamped at purchase,
  not authored on the prefab, so the placed fuel cans — never paid for — are worth nothing. Resale is
  always below cost, so buying and selling can never mint money.
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
| Better Flashlight | 180 | The same script, a beam that is genuinely brighter: range **70**, spot **60°** (inner **34°**), intensity **520 lm**, whiter |
| Shovel | 90 | A plain `Carryable`. No behaviour yet — the gameplay comes later |
| Adrenaline | **500, flat** | `Adrenaline` prices itself through `IStorePriced`, reading the one price on `Revival` rather than the stock list; see *Death and revive* |
| Fuel can | **50** | A detached copy of the scene cans (`FuelCan`, 40 fuel = 160 s). The only fuel once the four placed cans are used |
| UV flashlight | **200** | `UVFlashlight`, a `Flashlight` subclass. Deep violet beam (24 m, 32°, 110 lm) — narrower and shorter than the plain flashlight, and the **only** way to see the blood trail or the pool |
| Magic powder | **200** | `MagicPowder`. **Infinite** — bought once, used forever. Left mouse tips the jar; scatter it anywhere to test for hidden things, and on the pool of blood it brings the Level 2 door into the world |

- `StoreItem.CurrentPrice` is what is shown and charged, never `price` directly. A prefab that
  implements `IStorePriced` works its own price out; everything else uses the listed number.
  `TryBuy` reads the price **once, before spawning**, so what is shown and what is charged are the
  same number even for an item that works its own price out.

- **Light intensity here is in lumens (`m_LightUnit` 1), so a wider cone is a dimmer one.** This is
  what made the better flashlight feel identical to the plain one: 160 lm spread over 58° is only
  ~1.2× the plain lamp's 70 lm over 42° per unit area. Widening a beam without raising the lumens
  to match buys nothing. Compare the two as lumens ÷ cone solid angle, never as raw `intensity`.
- **Every flashlight is called a "flashlight".** `itemName` on the two store prefabs used to read
  `torch` / `heavy-duty torch`, which is the word the carrying line and the pickup prompt show — and
  the only place in the game that word appeared. The mesh objects inside the prefabs are still named
  `Torch_*`; those are never seen by the player.
- **The flashlight is store-only and no longer lies on the house floor.** The `Flashlight` scene root was
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
  replaces the cheapest piece on a Large-capable spot, so the item count is unchanged. `focus` is
  **deliberately empty now** — there is no fixed place of interest left to draw the Large piece
  towards, so it goes wherever depth sends it. Don't point it at anything: a landmark that always
  holds the television would be exactly the fixed objective this level was rebuilt to remove.
- **Nothing is ever placed inside the base.** `LootSpawner.IsInBase` rejects any spot the
  generator's radius covers — running or not, the same `IsInsideAnyRadius` question
  `MonsterSpawner` asks of its spawn points, because it is about the *ground* and not about
  whether anyone has switched the light on yet. So every piece worth money is a trip outside,
  and the house is somewhere to come back to rather than somewhere to loot. It is asked of the
  generator rather than measured against a radius of its own, so `protectionRadius` moves both
  together; `baseMargin` (0) widens it, `keepOutOfBase` turns it off.
  **The rule is runtime-only** — the registry behind it is filled in `OnEnable` — so
  `Rebuild Loot Spawn Points` still puts markers in the house and the yard. That is fine: a
  marker is not an item, and one inside the base simply goes unused. Don't move the check into
  `TryResolveSurface`, where it would silently do nothing for the editor tool.
- **Depth re-pairs; it never re-rolls.** The table and the shuffle choose the same items and spots as
  always; `AssignByDepth` then sends dearer pieces to spots further from `depthCentre` (the
  generator), blurred by chance by `1 − depthBias` (0.75). So rarity, caps and separation are
  untouched, and `depthBias` 0 is the old behaviour. The builder wires `depthCentre` and `focus`.
- **`LootSpawnPoint.extraDepth` is how the mountain pays better without a second rarity number.**
  Metres added to how deep a spot counts as, and it feeds the *pairing key* only. It exists because
  distance saturates at `deepRadius` (90) and the far woods and the cave are both out past that —
  measuring alone calls them equally deep, and they are not: the cave is the same walk with rock
  overhead and no moon in it. `DepthKey` is therefore deliberately allowed past 1; it only ever
  sorts spots against each other. **It cannot make a spot pay more often** — the table, the caps and
  the odds of anything being there at all are untouched, so it moves *which* of the run's pieces
  lands in the cave and never *whether* one does.
- **Measured over 2,000 runs of the real `SpawnRun`** with the defaults (`commonValue` 40, exponent
  1.4, 6–9 items a run, averaging 7.5):

  | item | price | size | avg/run | runs containing it | avg distance from generator |
  |---|---|---|---|---|---|
  | old radio | 40 | Small | 1.88 | 96.4% | 16 m |
  | camera | 60 | Small | 2.24 | 96.4% | 27 m |
  | old clock | 75 | Small | 1.88 | 92.0% | 39 m |
  | old CRT television | 300 | Large | 1.00 | **100%** (by the camp in 100%) | 40 m |
  | laptop | 150 | Medium | 0.51 | 42.4% | 44 m |

  Average haul **$727**. Those distances were measured *before* the base exclusion: the 2.2
  items (~$103) a run that used to lie inside the generator's 22 m are now redrawn onto spots
  outside it, so the haul and the table are unchanged and every column of distances is further
  out than it reads here. The laptop's
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
  always a base, and the fuel cans and flashlight are added to the taken-spots list so nothing spawns
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

- It samples a jittered grid over the whole house footprint, three rings round the house (yard
  9–20m, field 20–45m, deep 45–95m) and **eight discs inside the mountain**, and keeps
  whatever survives the probe. **It deliberately does not describe the rooms** —
  the probe rejects walls, partitions and furniture on its own, so what remains is exactly the
  walkable floor and the tops of the furniture.
- **Each region has its own cap.** A single shared budget is spent by whichever region is sampled
  first, which left the far field with zero points — and the far field is most of the reason to
  leave the house.
- **The mountain's regions come from `PrototypeEnvironmentBuilder.CaveLootRegions`**, off the same
  `CaveRuns` centreline the cave is carved from, so the two can never describe different caves and a
  rebuilt cave never leaves points standing in its new rock. The caps are thin, and **thinnest deep
  in** (mouth 2, main chamber 6, galleries 3 each, far gallery 3, vault 5 — 28 points in practice),
  with `extraDepth` rising 4 → 48 outward. Thin caps are the whole reason the place is worth walking
  into rather than worth farming: most of it is empty on any given night, and what is there is
  dearer per piece. `minItems`/`maxItems` went 6–9 → **7–11** so the new region is paid for rather
  than quietly thinning the woods. **Not balanced yet** — retune against a playtest and the haul.
- Expanding means adding a region there, or dropping a `LootSpawnPoint` in by hand: the spawner
  takes every one it can find, wherever it is parented.

### Lighting — a night you can walk by, woods that get darker

The goal: without a flashlight you can navigate and read the house, trees, paths and props, and it
still feels like night; the deeper into the woods, the darker, until the flashlight is what you see by.

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
  the work on purpose:** fog dims the flashlight beam as much as the moonlight, so darkening the woods
  with fog alone would make the flashlight useless exactly where it is needed. It is a per-viewer visual
  (nothing in the simulation reads fog or ambient), so in co-op each client runs its own. Each
  `LateUpdate` it writes `TimeOfDay`'s base ambient × its depth factor, never a multiple of what is
  already in `RenderSettings`, so it cannot compound — never write ambient from anywhere else.
- **`DarkQuarter` on `Systems` is the corner of the map you need a bought flashlight for.**
  A true quarter of the 200 × 200 square — one of the four corners, quadrants meeting at
  `mapCentre` (the origin, where the ground is centred) — **not** a wedge. It fades in over
  `edgeSoftness` **12 m** across each quadrant boundary, and from `innerRadius` **22 m** (the
  generator's own `protectionRadius`, so the dark starts exactly where the protection ends) to
  `fullRadius` **35 m**, so the yard is never dark whichever corner is drawn. At full strength it
  multiplies what `NightDepth` has already left: ambient × `ambientScale` (**0.28**), the moon ×
  `moonScale` (**0.05**) and fog × `fogScale` (1.25). Measured over 200 runs it covers **23.3%**
  of the map, **19.5%** of it at full strength — the rest of the quarter is the bite the
  generator's yard takes out of its inside corner, plus the soft edges.
- **`moonScale` is what actually makes it dark, and scaling ambient alone does not work.**
  Ambient only empties the *shadows*; the moon is a directional light and goes on picking out
  every surface facing it, so the first version (ambient 0.28, moon untouched) was measurably
  three and a half times darker and still perfectly readable in play — trunks, canopies and
  ground all legible. Taking the moon to **0.05** as well is what turns the corner into
  near-total black: verified by capture at 40 m, where shapes are only just sensed and nothing
  can be read. It is cloud over one corner of the sky, in effect. **Retune `moonScale` first**;
  `ambientScale` only moves what is already in shadow.
- The moon is written by `NightDepth` as `TimeOfDay.nightIntensity` × the factor, **never** as a
  multiple of what is already on the light, exactly as ambient is — so it cannot compound, and
  `NightDepth.OnDisable`'s `ApplyLighting()` hands the full moon back. It is a per-viewer visual
  like the rest of that script: each client lights its own scene and nothing in the simulation
  reads a light's intensity. Never write `sun.intensity` from anywhere else.
- **The flashlight is untouched by all three dials**, which is the whole point: a real light
  against a near-black corner reads enormously, and `fogScale` is kept at 1.25 because fog is
  the one dial that would dim the beam too.
- **In Level 1 the corner is named, not rolled: `fixedCorner` = SouthEast, because that is where the
  mountain is.** A mountain cannot move between runs, so the dark cannot either — the darkness there
  is the approach to the rock rather than weather. With a corner named, the avoidance below runs the
  other way round: `Level2Site` asks `DistanceFromDark` and rejects a bearing that would put the door
  in the dark quarter, which is only safe *because* a named corner cannot be moved by the asking.
  Everything in the two points below is what `Roll` still does, and setting it back to `Roll` brings
  all of it back untouched.
- **Which corner is drawn is a fresh roll every run, and it is even.** Measured over 2,000 runs:
  south-west 25.8%, south-east 25.1%, north-west 23.6%, north-east 25.6% (+z is north, +x east).
  It is a reservoir draw over the corners that survive the trail check — one pass, no attempt
  loop. `logChoice` prints the corner at run start ("the north-west quarter of the map is the
  dark one this run"), which is the only way to know without walking there; selecting `Systems`
  draws it in the Scene view.
- **The corner the Level 2 trail runs into is struck out, and so is any corner within
  `clearance` (20 m) of the door.** Measured over 2,000 runs with the trail on every bearing,
  the door fell in *any* darkness at all **0 times**. `DoorDistanceTo` measures point-to-quadrant,
  so "in it" and "nearly in it" are one number; if every corner were somehow refused it falls
  back to the one furthest from the door rather than failing.
- **It bites close in, and that is the whole reason `fullRadius` is 35 m.** Ambient outside →
  inside runs **40 m 0.86 → 0.24**, 50 m 0.71 → 0.20, 70 m 0.40 → 0.11. An early version only
  reached full strength at 55 m and was **invisible**: captures at 70 m with and against it are
  indistinguishable, because the woods out there are already near-black and ambient has nothing
  left to take. The band that still reads by moonlight — roughly 25–55 m — is the only band
  worth darkening, so never push `fullRadius` back out.
- **A line of twenty-five barely-visible drops of blood hidden in the darkest part of the map
  would make the only thread the player has a matter of luck** — that is why the trail check
  exists at all, and why it is a strike-out rather than a retry.
- **The corner is drawn on the first frame, not in `Start`.** `Level2Site` rolls the trail's
  bearing in *its* `Start`, and the order of two `Start`s is undefined — so `DarkQuarter`
  chooses lazily, the first time anything asks `Corner` or `Weight`, by which point the trail
  has certainly been placed. Same reasoning as `Level2Site.DoorSpotClear`: never depend on
  execution order. The *choice* is authority-owned shared state (`Instance`, `HasAuthority`);
  the darkness itself is drawn per viewer by `NightDepth`, which stays the only writer of
  ambient and fog. `Weight(point)` is a pure query, so a HUD or a future monster that hunts the
  dark can ask it freely.
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

Row numbers are claimed and must not collide. **Left column** (`Hud.Row`): **0** the crouch readout
(`PlayerMovement`, only while crouched), **1** stamina (`PlayerMovement`, only while below full), **2** Generator, **3** `PlayerVitals` (`DEAD`, or `[SAFE]` while inside the generator's light — there is no health line), **4** MonsterSpawner, **5 and down** `MonsterDebugHud`
(last noise, then one line per monster — set its `firstRow` if you need row 5 back).
**Right column** (`Hud.RowRight`):
**0** money balance, **1** the `+$100` change popup, both drawn by `MoneyHud`, **2** the
`LevelCompleteHud` line once the map shrinks away, **3** the field-revive timer (`Revival`, only
while an injection is running), **4** the key maker's `n/4` (`KeyMaker`, only once the first
fragment is in). The two columns are numbered separately, so they
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

- Trigger a build, unless asked.

**Play mode is always allowed, and is the expected way to verify gameplay.** Anything that only
exists while the game runs — `Awake`/`OnEnable` registries, `UVFlashlight.AnyLit`, `Level2Site`'s
rolled bearing, a monster's state machine — cannot be checked from edit mode, and guessing at it
from the Inspector has cost real time. Enter Play mode, look, and exit; leave the Editor out of
Play mode when you are done and never save the scene while it is running.
- Reformat or restructure files you weren't asked to touch.
- Add packages to `Packages/manifest.json` without asking.
- Delete assets or scene objects that weren't part of the request.
