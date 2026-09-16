using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Rebuilds the whole playable environment - abandoned house, forest, props and
/// night atmosphere - out of Unity primitives only.
///
/// It never destroys the Player, the Systems clock/spawner, the Generator or the
/// fuel cans: those are kept and only repositioned/retuned, so no gameplay wiring
/// is lost. Everything else at the scene root is replaceable scenery.
/// </summary>
public static class PrototypeEnvironmentBuilder
{
    // ---------------------------------------------------------------- layout
    // House footprint. The origin sits at the centre of the building.
    const float HouseHalfX = 7.0f;    // x in [-7, 7]
    const float HouseHalfZ = 5.5f;    // z in [-5.5, 5.5]
    const float FloorTop = 0.05f;     // same reference height as the old level
    const float GroundTop = 0.04f;    // 1 cm under the floors so faces don't z-fight
    const float WallHeight = 3.0f;
    const float ExtThick = 0.15f;
    const float IntThick = 0.12f;
    const float RoofY = 3.08f;

    // Interior partitions.
    const float HallWestX = -1.0f;    // hallway's west wall
    const float HallEastX = 1.6f;     // hallway's east wall
    const float FoyerKitchenZ = -1.5f;
    const float LivingBedroomZ = 0.5f;

    const float GroundHalf = 100f;    // playable square; the boundary sits on its edges

    // Forest bands. Density drops with distance on purpose: the near woods have to read as
    // a wall you push through, but a 200 m map at that density is thousands of renderers
    // and a NavMesh bake to match. Thinner far out is what keeps the map affordable.
    const float ForestInner = 14f;    // trees thin out this close, so the clearing reads as one
    const float ForestNear = 55f;     // dense stand around the house
    const float ForestMid = 88f;      // thinner, the long walks
    const float ForestEdge = 3f;      // the outer band stops this far short of the boundary

    static readonly Vector3 GeneratorPos = new Vector3(8.8f, 0f, -7.2f);
    static readonly Vector3 PlayerSpawn = new Vector3(4.7f, 1.4f, -12.5f);

    // Objects that carry gameplay wiring. Never destroyed, only repositioned.
    static readonly HashSet<string> Keep = new HashSet<string>
    {
        "Player", "Systems", "Directional Light", "Global Volume",
        "Generator", "PlayerRespawn", "FuelCans", "Flashlight",
        "SellStation", "Store", "Valuables",
        // Hand-authored monster wiring: MonsterSpawner.patrolRoute points at PatrolRoute.
        "PatrolRoute", "Monster_Test"
    };

    // ------------------------------------------------------------- materials
    const string MatDir = "Assets/Materials/Env";
    const string ObjectivePrefabDir = "Assets/Prefabs/Objective";

    static Material grass, dirt, wallExt, wallInt, floorWood, roofMat, wood, plank,
                    fabric, metalDark, metalPale, glass, bark, foliage, foliageAlt,
                    rockMat, bushMat, lampWarm, lampDead, ceramic, paint, caveFloor;

    // Trunk positions, so props and the path can avoid growing inside a tree.
    static readonly List<Vector2> occupied = new List<Vector2>();

    // Old logging tracks, as polylines from the house clearing outward. They fork rather than
    // radiate, so the map reads as a place people used rather than a hub, and they all give
    // out somewhere in the trees -- they are something to navigate by, never a route to
    // anything. Nothing is placed at their ends on purpose: a track that led to the objective
    // would tell the player where to look.
    static readonly Vector2[][] Trails =
    {
        // north-west: past the shed to the woodcutter's stand, on to the ruins
        new[] { new Vector2(-4f, 11f), new Vector2(-14f, 21f), new Vector2(-26f, 30f), new Vector2(-44f, 34f), new Vector2(-58f, 36f) },
        // north-east: out to the fallen-roof cabin, then north to the hunting stand
        new[] { new Vector2(5f, 12f), new Vector2(12f, 30f), new Vector2(18f, 52f), new Vector2(-6f, 64f), new Vector2(-30f, 70f) },
        // east: the garage, forking north to the culvert and back out into the trees. The
        // south-east fork used to run on to the far house; the mountain stands where that
        // house was, and the track now gives out well short of the rock -- a track that ended
        // at the cave mouth would be a signpost to the one thing in the level worth finding
        // on your own.
        new[] { new Vector2(11f, -7f), new Vector2(24f, -16f), new Vector2(34f, -22f) },
        new[] { new Vector2(34f, -22f), new Vector2(46f, -4f), new Vector2(52f, 22f) },
        new[] { new Vector2(34f, -22f), new Vector2(44f, -14f), new Vector2(50f, -2f) },
        // south: down past the fence line to the quarry, curving away from the rock
        new[] { new Vector2(4.7f, -21f), new Vector2(8f, -46f), new Vector2(0f, -60f), new Vector2(-10f, -70f) },
        // west and south-west: the wreck to the green-door cabin, and a track that gives out
        // well short of Tom's camp -- the paint marks take over from there
        new[] { new Vector2(-12.5f, -14f), new Vector2(-28f, -12f), new Vector2(-44f, -6f) },
        new[] { new Vector2(-14f, -24f), new Vector2(-28f, -42f), new Vector2(-38f, -58f) },
    };

    const float TrailHalfWidth = 1.1f;    // the dirt strip itself
    const float TrailClear = 2.2f;        // nothing grows this close to the centreline

    // ------------------------------------------------------------- the mountain
    // The south-east quarter of the map is rock rather than woods: a mass you cannot walk
    // over, cannot see into, and can only get inside through one mouth in its face.
    //
    // Everything about it is laid out in (s, t) rather than (x, z), because the thing it has
    // to fit is a CORNER of a square map. s runs from the house out along the diagonal towards
    // the corner, t runs across it. In those coordinates the quarter is simply "s past the
    // front", and the map's own edges are |t| <= MountainReach - s, so the mass narrows to a
    // point at the corner without a single hand-written boundary number.
    const float MountainFront = 52f;               // the rock face, measured along the corner axis
    const float MountainReach = 141.4214f;         // GroundHalf * sqrt(2): the corner itself
    const float MountainCell = 6f;                 // grid pitch of the rock mass
    const float MountainOverlap = 1.4f;            // blocks overlap, so no two ever leave a gap to squeeze through
    const float MountainBaseHeight = 11f;          // at the face: already unclimbable
    const float MountainPeakHeight = 37f;          // at the corner
    const float CaveCeiling = 6.5f;                // rock overhead: the floor of every block above a void
    const float MountainClear = 6f;                // no tree grows this close to the face
    const float CaveDarkSoftness = 4f;             // metres of fade at the mouth, and the volumes' overlap

    /// <summary>
    /// The cave, as runs of (s, t, carve) -- a centreline and how far the rock is pushed back
    /// along it. Every block whose centre falls inside a carve is simply not built, so the
    /// passages ARE the gaps in the mass and there is no second description of the cave's
    /// shape to keep in step with the first. The dark volumes, the floor, the loot regions and
    /// the NavMesh cut-out are all read off these same numbers.
    ///
    /// The first run starts in FRONT of the face, which is what opens the mouth; the last one
    /// is the bit behind the hidden door, and is reachable only once the powder has found it.
    /// </summary>
    static readonly Vector3[][] CaveRuns =
    {
        // the way in: a mouth in the face, bending twice so the forest is out of sight in
        // a few strides, into the chamber the whole cave hangs off
        new[] { new Vector3(52f, 23f, 6.6f), new Vector3(58f, 19f, 6.6f), new Vector3(66f, 12f, 6.8f), new Vector3(74f, 6f, 11f) },
        // a side gallery running back towards the face: a dead end, and the widest empty space
        new[] { new Vector3(74f, 6f, 11f), new Vector3(80f, 20f, 6.6f), new Vector3(86f, 27f, 8.5f) },
        // a short hollow off the other side of the chamber, also a dead end
        new[] { new Vector3(74f, 6f, 11f), new Vector3(73f, -8f, 6.6f), new Vector3(76f, -18f, 8f) },
        // deeper: the run the marks follow, out to the far gallery
        new[] { new Vector3(74f, 6f, 11f), new Vector3(82f, -4f, 6.6f), new Vector3(92f, -12f, 6.6f),
                new Vector3(102f, -10f, 6.6f), new Vector3(110f, -16f, 8.5f) },
        // through the back wall of the far gallery, and what is behind it
        new[] { new Vector3(110f, -16f, 8.5f), new Vector3(MountainDoorS, MountainDoorT, 6.4f), new Vector3(120f, -8f, 7.5f) },
    };

    // A carve is measured to a block's CENTRE, so what is actually walkable is the carve less
    // half a block less the jitter: at carve 6.6 a passage comes out about 4.6 m across. Take
    // any of these much below 6 and the grid starts pinching passages shut on the diagonal,
    // where a player one metre wide cannot get through at all.

    // Where the far gallery's back wall stands, and which way through it faces.
    const float MountainDoorS = 114f;
    const float MountainDoorT = -14f;

    /// <summary>
    /// The line the UV marks follow: out of the chamber where the note is, along the deep run,
    /// stopping a couple of metres short of a wall with nothing in it.
    /// </summary>
    static readonly Vector2[] CaveTrail =
    {
        new Vector2(72f, 10f), new Vector2(74f, 6f), new Vector2(82f, -4f), new Vector2(92f, -12f),
        new Vector2(102f, -10f), new Vector2(110f, -16f), new Vector2(112.6f, -15.3f)
    };

    /// <summary>
    /// The mouth of the cave, in world coordinates: the one way in. Where the first run crosses
    /// the face of the rock, which is half a block in front of the first row of blocks.
    /// </summary>
    public static Vector3 CaveMouth { get { return Mountain(51.5f, 23.4f); } }

    /// <summary>World position of a point on the mountain's own (s, t) grid, at ground level.</summary>
    static Vector3 Mountain(float s, float t)
    {
        const float k = 0.7071068f;
        return new Vector3(k * (s + t), GroundTop, k * (t - s));
    }

    /// <summary>How far out along the corner axis a world point lies. The face is at MountainFront.</summary>
    static float MountainS(Vector2 p)
    {
        return 0.7071068f * (p.x - p.y);
    }

    /// <summary>
    /// Is this (s, t) inside one of the cave's passages? The one definition -- the mass, the
    /// floor, the dark and the NavMesh cut-out all ask it, so they cannot describe different
    /// caves.
    /// </summary>
    static bool InCave(float s, float t, float margin)
    {
        Vector2 p = new Vector2(s, t);

        foreach (Vector3[] run in CaveRuns)
        {
            for (int i = 0; i < run.Length - 1; i++)
            {
                Vector2 a = new Vector2(run[i].x, run[i].y);
                Vector2 b = new Vector2(run[i + 1].x, run[i + 1].y);

                Vector2 ab = b - a;
                float lengthSq = ab.sqrMagnitude;
                float u = lengthSq < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);

                float carve = Mathf.Lerp(run[i].z, run[i + 1].z, u);
                if (Vector2.Distance(p, a + ab * u) < carve + margin) return true;
            }
        }
        return false;
    }

    [MenuItem("Lab/Environment/Build Prototype Environment")]
    public static void Build()
    {
        if (Shader.Find("Universal Render Pipeline/Lit") == null)
        {
            Debug.LogError("URP Lit shader not found - is the Universal RP package active?");
            return;
        }

        GameObject player = Find("Player");
        GameObject systems = Find("Systems");
        GameObject generator = Find("Generator");
        GameObject sunGo = Find("Directional Light");
        if (player == null || systems == null || generator == null)
        {
            Debug.LogError("Expected Player, Systems and Generator in the scene; aborting so nothing is lost.");
            return;
        }

        Undo.SetCurrentGroupName("Build prototype environment");
        int group = Undo.GetCurrentGroup();

        Random.InitState(20260907);
        occupied.Clear();
        MakeMaterials();

        ClearOldEnvironment();

        Transform ground = BuildGround();
        BuildBoundary(ground);
        Transform house = BuildHouse();
        List<Light> houseLights = BuildHouseLights(house);
        BuildFurniture(house);
        BuildForest();
        BuildMountain();
        BuildProps();

        // After every collider exists and before anything reads the level: loot points are
        // probed against real geometry, so a rebuilt house must not keep the old ones.
        LootSpawnPointBuilder.Build();

        PlaceGameplayObjects(player, systems, generator, sunGo, houseLights);
        ApplyAtmosphere();
        RebakeNavMesh(systems);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Undo.CollapseUndoOperations(group);

        Debug.Log("Prototype environment rebuilt: Ground, Boundary, House, Forest, Props, " +
                  "LootSpawnPoints. Player, Systems, Generator and FuelCans were kept and repositioned.");
    }

    // ------------------------------------------------------------------ util
    static GameObject Find(string name)
    {
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static void ClearOldEnvironment()
    {
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (!Keep.Contains(go.name)) Undo.DestroyObjectImmediate(go);
    }

    static Transform Root(string name)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        return go.transform;
    }

    static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Prim(Transform parent, string name, PrimitiveType type,
                           Vector3 pos, Vector3 scale, Material mat,
                           Vector3 euler = default(Vector3), bool collider = true)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;

        Collider c = go.GetComponent<Collider>();
        if (!collider && c != null) Object.DestroyImmediate(c);

        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }

    /// <summary>Axis-aligned box given by its world extents - easier to reason about than centre+scale.</summary>
    static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1,
                          float z0, float z1, Material mat, bool collider = true)
    {
        return Prim(parent, name, PrimitiveType.Cube,
                    new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                    new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)),
                    mat, default(Vector3), collider);
    }

    static Material Mat(string name, Color baseColor, float smoothness, float metallic, Color emission)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "Env");

        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");

        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = sh;
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);

        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void MakeMaterials()
    {
        // Everything is deliberately near-black. The level is lit by one generator
        // and a sliver of moon, so albedo has to leave headroom for that.
        grass      = Mat("Env_Grass",       new Color(0.055f, 0.075f, 0.045f), 0.05f, 0f, Color.black);
        dirt       = Mat("Env_Dirt",        new Color(0.105f, 0.085f, 0.065f), 0.03f, 0f, Color.black);
        wallExt    = Mat("House_WallExt",   new Color(0.150f, 0.145f, 0.130f), 0.05f, 0f, Color.black);
        wallInt    = Mat("House_WallInt",   new Color(0.230f, 0.215f, 0.190f), 0.08f, 0f, Color.black);
        floorWood  = Mat("House_Floor",     new Color(0.125f, 0.100f, 0.075f), 0.12f, 0f, Color.black);
        roofMat    = Mat("House_Roof",      new Color(0.085f, 0.085f, 0.095f), 0.05f, 0f, Color.black);
        wood       = Mat("House_Wood",      new Color(0.175f, 0.125f, 0.085f), 0.10f, 0f, Color.black);
        plank      = Mat("House_Plank",     new Color(0.215f, 0.170f, 0.125f), 0.08f, 0f, Color.black);
        fabric     = Mat("House_Fabric",    new Color(0.135f, 0.115f, 0.105f), 0.02f, 0f, Color.black);
        metalDark  = Mat("Env_MetalDark",   new Color(0.115f, 0.115f, 0.125f), 0.45f, 0.85f, Color.black);
        metalPale  = Mat("Env_MetalPale",   new Color(0.420f, 0.425f, 0.410f), 0.60f, 0.75f, Color.black);
        ceramic    = Mat("Env_Ceramic",     new Color(0.520f, 0.520f, 0.500f), 0.75f, 0f, Color.black);
        glass      = Mat("Env_GlassDark",   new Color(0.030f, 0.040f, 0.050f), 0.92f, 0f, Color.black);
        bark       = Mat("Tree_Bark",       new Color(0.070f, 0.055f, 0.045f), 0.03f, 0f, Color.black);
        foliage    = Mat("Tree_Foliage",    new Color(0.035f, 0.060f, 0.040f), 0.04f, 0f, Color.black);
        foliageAlt = Mat("Tree_FoliageAlt", new Color(0.050f, 0.065f, 0.045f), 0.04f, 0f, Color.black);
        rockMat    = Mat("Env_Rock",        new Color(0.105f, 0.105f, 0.115f), 0.10f, 0f, Color.black);
        // Under the mountain: colder and flatter than the boulders outside, so a cave wall in
        // a flashlight beam does not read as the same grey as a rock in the moonlight.
        caveFloor  = Mat("Env_CaveFloor",   new Color(0.080f, 0.075f, 0.070f), 0.04f, 0f, Color.black);
        bushMat    = Mat("Env_Bush",        new Color(0.040f, 0.058f, 0.038f), 0.03f, 0f, Color.black);
        lampWarm   = Mat("Light_WarmBulb",  new Color(0.320f, 0.230f, 0.130f), 0.30f, 0f, new Color(3.2f, 2.0f, 0.85f));
        lampDead   = Mat("Light_DeadBulb",  new Color(0.180f, 0.180f, 0.170f), 0.60f, 0f, Color.black);
        // Pale, so Tom's trail marks and paper read in a flashlight beam against near-black bark.
        paint      = Mat("Env_Paint",       new Color(0.420f, 0.410f, 0.370f), 0.10f, 0f, Color.black);
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- ground
    static Transform BuildGround()
    {
        Transform t = Root("Ground");

        // A scaled cube rather than a Plane, so the boundary can be derived from
        // real renderer bounds and the top can sit just under the house floors.
        Prim(t, "Ground_Surface", PrimitiveType.Cube,
             new Vector3(0f, GroundTop - 1f, 0f),
             new Vector3(GroundHalf * 2f, 2f, GroundHalf * 2f), grass);

        // A worn path up to the porch, so the approach reads even in the dark.
        Box(t, "Ground_Path", 3.4f, 6.0f, GroundTop, GroundTop + 0.02f, -21f, -5.6f, dirt, false);
        Box(t, "Ground_Yard", 1.4f, 10.6f, GroundTop, GroundTop + 0.02f, -10.5f, -5.6f, dirt, false);

        BuildTrails(t);
        return t;
    }

    /// <summary>
    /// Re-bake the walkable surface. Every wall, trunk and rock this script just replaced
    /// was an input to it, so a rebuild without this leaves the monsters routing around
    /// forest that is no longer there. The NavMeshSurface lives on Systems, which is in the
    /// Keep set, so its settings and its baked asset survive the clear.
    /// </summary>
    static void RebakeNavMesh(GameObject systems)
    {
        NavMeshSurface surface = systems.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogWarning("No NavMeshSurface on Systems: monsters will roam in straight lines. " +
                             "Add one (collect All, geometry Physics Colliders) and rebuild.");
            return;
        }

        surface.BuildNavMesh();
        if (surface.navMeshData != null) EditorUtility.SetDirty(surface.navMeshData);
        AssetDatabase.SaveAssets();
    }

    static void BuildBoundary(Transform ground)
    {
        // Derived from the ground's actual renderer bounds, never hardcoded, so a
        // resized ground can have its walls rebuilt from the same numbers.
        Bounds b = ground.GetChild(0).GetComponent<Renderer>().bounds;

        Transform t = Root("Boundary");
        const float h = 12f;
        const float thick = 1f;

        MakeWall("Boundary_North", new Vector3(b.center.x, h * 0.5f, b.max.z + thick * 0.5f), new Vector3(b.size.x + thick * 2f, h, thick));
        MakeWall("Boundary_South", new Vector3(b.center.x, h * 0.5f, b.min.z - thick * 0.5f), new Vector3(b.size.x + thick * 2f, h, thick));
        MakeWall("Boundary_East",  new Vector3(b.max.x + thick * 0.5f, h * 0.5f, b.center.z), new Vector3(thick, h, b.size.z + thick * 2f));
        MakeWall("Boundary_West",  new Vector3(b.min.x - thick * 0.5f, h * 0.5f, b.center.z), new Vector3(thick, h, b.size.z + thick * 2f));

        void MakeWall(string name, Vector3 pos, Vector3 size)
        {
            GameObject go = new GameObject(name);   // collider only: nothing to render
            go.transform.SetParent(t, false);
            go.transform.localPosition = pos;
            go.AddComponent<BoxCollider>().size = size;
        }
    }

    // ----------------------------------------------------------------- house
    /// <summary>A hole in a wall run: a doorway (bottom 0) or a window (bottom above the floor).</summary>
    struct Opening
    {
        public float centre;   // world coordinate along the wall's run axis
        public float width;
        public float bottom;   // height above the floor top
        public float top;

        public static Opening Door(float centre, float width = 1.2f) { return new Opening { centre = centre, width = width, bottom = 0f, top = 2.2f }; }
        public static Opening Window(float centre, float width = 1.3f) { return new Opening { centre = centre, width = width, bottom = 1.0f, top = 2.1f }; }
    }

    /// <summary>
    /// Builds one axis-aligned wall run, cut by its openings. All walls in this
    /// house are axis-aligned, so the run needs no rotation: for a wall along x
    /// the box's x is its length, for a wall along z its z is.
    /// </summary>
    static void WallRun(Transform parent, string name, Vector3 a, Vector3 b,
                        float thickness, Material mat, params Opening[] openings)
    {
        bool alongX = Mathf.Approximately(a.z, b.z);
        float runStart = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.z, b.z);
        float runEnd = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.z, b.z);
        float fixedAxis = alongX ? a.z : a.x;

        float yBase = FloorTop;
        float yTop = FloorTop + WallHeight;

        List<Opening> ops = new List<Opening>(openings);
        ops.Sort((p, q) => p.centre.CompareTo(q.centre));

        int piece = 0;
        float cursor = runStart;

        foreach (Opening o in ops)
        {
            float left = o.centre - o.width * 0.5f;
            float right = o.centre + o.width * 0.5f;

            if (left > cursor) Piece(cursor, left, yBase, yTop);
            if (o.bottom > 0f) Piece(left, right, yBase, yBase + o.bottom);            // sill under a window
            if (o.top < WallHeight) Piece(left, right, yBase + o.top, yTop);           // lintel over the hole
            cursor = right;
        }
        if (cursor < runEnd) Piece(cursor, runEnd, yBase, yTop);

        void Piece(float p0, float p1, float y0, float y1)
        {
            string n = name + "_" + (piece++);
            if (alongX) Box(parent, n, p0, p1, y0, y1, fixedAxis - thickness * 0.5f, fixedAxis + thickness * 0.5f, mat);
            else Box(parent, n, fixedAxis - thickness * 0.5f, fixedAxis + thickness * 0.5f, y0, y1, p0, p1, mat);
        }
    }

    /// <summary>
    /// The door pattern: an empty hinge at the edge of the opening carrying
    /// DoorInteraction, with the leaf as its child at localPosition (0.6, 0, 0).
    /// openAngle 90 rotates the hinge's local +x toward its local -z.
    /// </summary>
    static void Door(Transform parent, string name, Vector3 hingePos, float hingeYaw,
                     bool canBeForced = true)
    {
        GameObject hinge = new GameObject(name + "_Hinge");
        hinge.transform.SetParent(parent, false);
        hinge.transform.localPosition = hingePos;
        hinge.transform.localEulerAngles = new Vector3(0f, hingeYaw, 0f);

        // Kept out of the NavMesh bake: a door swings, and a mesh baked round a shut one
        // would leave every room an unreachable island for the rest of the night.
        hinge.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Targeting is PlayerInteractor's job, so the hinge needs nothing wired.
        DoorInteraction di = hinge.AddComponent<DoorInteraction>();
        di.openAngle = 90f;
        di.canBeForced = canBeForced;

        GameObject leaf = Prim(hinge.transform, name + "_Leaf", PrimitiveType.Cube,
                               new Vector3(0.6f, 0f, 0f), new Vector3(1.1f, 2.2f, 0.15f), wood);

        // A handle, so which side opens is readable up close.
        Prim(leaf.transform, name + "_Handle", PrimitiveType.Sphere,
             new Vector3(0.38f, 0f, -0.6f), new Vector3(0.07f, 0.035f, 0.5f), metalPale,
             default(Vector3), false);
    }

    static Transform BuildHouse()
    {
        Transform house = Root("House");

        Transform shell = Group(house, "Shell");
        Transform walls = Group(house, "Walls");
        Transform doors = Group(house, "Doors");
        Transform porch = Group(house, "Porch");
        Transform detail = Group(house, "Detail");

        float x0 = -HouseHalfX, x1 = HouseHalfX;
        float z0 = -HouseHalfZ, z1 = HouseHalfZ;

        // --- shell -----------------------------------------------------------
        Box(shell, "Foundation", x0 - 0.35f, x1 + 0.35f, -0.55f, FloorTop, z0 - 0.35f, z1 + 0.35f, rockMat);
        Box(shell, "Floor", x0 - 0.075f, x1 + 0.075f, -0.05f, FloorTop, z0 - 0.075f, z1 + 0.075f, floorWood);
        Box(shell, "Ceiling", x0 - 0.075f, x1 + 0.075f, RoofY - 0.08f, RoofY + 0.08f, z0 - 0.075f, z1 + 0.075f, roofMat);

        // Pitched roof above the flat ceiling: two tilted slabs meeting at a ridge
        // along x. Rise is 1.44 over a 5.8 run, so the tilt is ~14 degrees.
        const float eave = 5.8f, rise = 1.44f;
        float slope = Mathf.Sqrt(eave * eave + rise * rise);
        float tilt = Mathf.Atan2(rise, eave) * Mathf.Rad2Deg;
        Prim(shell, "Roof_South", PrimitiveType.Cube,
             new Vector3(0f, RoofY + rise * 0.5f + 0.1f, -eave * 0.5f),
             new Vector3(HouseHalfX * 2f + 1.2f, 0.16f, slope), roofMat, new Vector3(-tilt, 0f, 0f));
        Prim(shell, "Roof_North", PrimitiveType.Cube,
             new Vector3(0f, RoofY + rise * 0.5f + 0.1f, eave * 0.5f),
             new Vector3(HouseHalfX * 2f + 1.2f, 0.16f, slope), roofMat, new Vector3(tilt, 0f, 0f));
        Box(shell, "Chimney", -4.9f, -3.9f, RoofY, RoofY + 2.4f, -2.6f, -1.6f, rockMat);
        Box(shell, "Chimney_Cap", -5.05f, -3.75f, RoofY + 2.4f, RoofY + 2.6f, -2.75f, -1.45f, rockMat);

        // --- exterior walls --------------------------------------------------
        WallRun(walls, "Ext_South", new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), ExtThick, wallExt,
                Opening.Door(4.3f), Opening.Window(-4.6f, 1.4f), Opening.Window(0.3f, 1.1f));
        WallRun(walls, "Ext_North", new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), ExtThick, wallExt,
                Opening.Window(-4.2f, 1.3f), Opening.Window(4.2f, 1.5f));
        WallRun(walls, "Ext_West", new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), ExtThick, wallExt,
                Opening.Window(-3.0f, 1.4f), Opening.Window(3.2f, 1.3f));
        WallRun(walls, "Ext_East", new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), ExtThick, wallExt,
                Opening.Window(-3.6f, 1.2f), Opening.Window(2.6f, 1.4f));

        // --- interior partitions ---------------------------------------------
        // Hallway runs south to north between HallWestX and HallEastX and links
        // every room, so no space is a dead end off the entrance.
        WallRun(walls, "Int_HallWest", new Vector3(HallWestX, 0f, z0), new Vector3(HallWestX, 0f, z1), IntThick, wallInt,
                Opening.Door(-3.0f), Opening.Door(3.0f));
        WallRun(walls, "Int_HallEast", new Vector3(HallEastX, 0f, z0), new Vector3(HallEastX, 0f, z1), IntThick, wallInt,
                new Opening { centre = -3.5f, width = 1.4f, bottom = 0f, top = 2.3f },   // archway from the entrance
                new Opening { centre = 2.2f, width = 1.6f, bottom = 0f, top = 2.3f });   // archway into the kitchen
        WallRun(walls, "Int_FoyerKitchen", new Vector3(HallEastX, 0f, FoyerKitchenZ), new Vector3(x1, 0f, FoyerKitchenZ), IntThick, wallInt);
        WallRun(walls, "Int_LivingBedroom", new Vector3(x0, 0f, LivingBedroomZ), new Vector3(HallWestX, 0f, LivingBedroomZ), IntThick, wallInt);

        // --- doors -------------------------------------------------------------
        // Front door hinges on the west edge of its opening with yaw 0, so its
        // leaf runs +x and swings out onto the porch, clear of the foyer.
        Door(doors, "FrontDoor", new Vector3(3.7f, FloorTop + 1.1f, z0), 0f);
        // Both interior doors hinge with yaw 90 (leaf along -z) so they swing west
        // into the room rather than sweeping the narrow hallway.
        Door(doors, "LivingDoor", new Vector3(HallWestX, FloorTop + 1.1f, -2.4f), 90f);
        Door(doors, "BedroomDoor", new Vector3(HallWestX, FloorTop + 1.1f, 3.6f), 90f);

        // --- windows: dark glass, half of them boarded over ---------------------
        Glass(detail, "Win_S_Living", -4.6f, 1.4f, z0, true, true);
        Glass(detail, "Win_S_Hall", 0.3f, 1.1f, z0, true, false);
        Glass(detail, "Win_N_Bedroom", -4.2f, 1.3f, z1, true, false);
        Glass(detail, "Win_N_Kitchen", 4.2f, 1.5f, z1, true, true);
        Glass(detail, "Win_W_Living", -3.0f, 1.4f, x0, false, false);
        Glass(detail, "Win_W_Bedroom", 3.2f, 1.3f, x0, false, true);
        Glass(detail, "Win_E_Foyer", -3.6f, 1.2f, x1, false, true);
        Glass(detail, "Win_E_Kitchen", 2.6f, 1.4f, x1, false, false);

        // --- porch ---------------------------------------------------------------
        Box(porch, "Porch_Deck", 2.0f, 7.4f, -0.15f, FloorTop, -8.2f, z0, plank);
        Box(porch, "Porch_Step1", 3.2f, 6.2f, -0.20f, -0.02f, -8.6f, -8.2f, plank);
        Box(porch, "Porch_Step2", 3.2f, 6.2f, -0.35f, -0.19f, -9.0f, -8.6f, plank);
        Prim(porch, "Porch_Post_W", PrimitiveType.Cylinder, new Vector3(2.35f, 1.4f, -7.95f), new Vector3(0.14f, 1.35f, 0.14f), wood);
        Prim(porch, "Porch_Post_E", PrimitiveType.Cylinder, new Vector3(7.05f, 1.4f, -7.95f), new Vector3(0.14f, 1.35f, 0.14f), wood);
        Box(porch, "Porch_Roof", 1.8f, 7.6f, 2.72f, 2.86f, -8.5f, z0, roofMat);
        Box(porch, "Porch_Rail_W", 2.2f, 2.5f, 0.9f, 1.0f, -8.1f, z0, plank);
        Box(porch, "Porch_Rail_E", 6.9f, 7.2f, 0.9f, 1.0f, -8.1f, z0, plank);
        // One board torn loose, lying where it fell.
        Prim(porch, "Porch_LooseBoard", PrimitiveType.Cube, new Vector3(1.4f, 0.12f, -9.4f),
             new Vector3(1.8f, 0.06f, 0.24f), plank, new Vector3(0f, 24f, 6f));

        // A shutter hanging off one hinge: a cheap decay cue.
        Prim(detail, "Shutter_Loose", PrimitiveType.Cube, new Vector3(x0 - 0.2f, 1.9f, -2.1f),
             new Vector3(0.06f, 1.1f, 0.7f), plank, new Vector3(0f, 0f, 14f));

        // The nailed-up notice beside the front door: the one thing in the level anyone has
        // to read, and all it has to say is where to go. It needs a collider to be looked at.
        GameObject notice = Prim(detail, "Notice", PrimitiveType.Cube, new Vector3(3.35f, 1.85f, z0 - 0.1f),
                                 new Vector3(0.34f, 0.46f, 0.02f), plank, new Vector3(0f, 0f, -7f));
        Readable note = notice.AddComponent<Readable>();
        note.prompt = "Read the notice";
        note.title = "Notice";
        // The whole tutorial, in three lines: the gate is locked, the key is in four pieces,
        // and they are out there somewhere. It deliberately does NOT say where -- nothing in
        // the level does, because the fragments move every run.
        note.text = "KEEP THE GENERATOR RUNNING.\nDON'T RUN.\n\n" +
                    "The gate's chained and we broke the key up between us. Four pieces. " +
                    "We went out to hide them and not all of us came back.\n\n" +
                    "Find all four, fit them in the lock, and go.\n\n" +
                    "- R.";

        return house;
    }

    /// <summary>Glass pane in a window opening, optionally boarded over from outside.</summary>
    static void Glass(Transform parent, string name, float centre, float width, float fixedAxis,
                      bool alongX, bool boarded)
    {
        float y0 = FloorTop + 1.0f, y1 = FloorTop + 2.1f;
        float outward = fixedAxis > 0f ? 1f : -1f;

        GameObject pane = alongX
            ? Box(parent, name + "_Pane", centre - width * 0.5f, centre + width * 0.5f, y0, y1, fixedAxis - 0.02f, fixedAxis + 0.02f, glass, false)
            : Box(parent, name + "_Pane", fixedAxis - 0.02f, fixedAxis + 0.02f, y0, y1, centre - width * 0.5f, centre + width * 0.5f, glass, false);

        // Glass lets the room lamps out, so a lit house throws window-shaped pools onto
        // the yard. Boards still cast, which is what slats the light on the boarded ones.
        pane.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (!boarded) return;

        for (int i = 0; i < 3; i++)
        {
            float y = Mathf.Lerp(y0 + 0.15f, y1 - 0.15f, i / 2f);
            float tilt = Random.Range(-9f, 9f);
            Vector3 pos = alongX
                ? new Vector3(centre, y, fixedAxis + outward * 0.12f)
                : new Vector3(fixedAxis + outward * 0.12f, y, centre);
            Vector3 scale = alongX
                ? new Vector3(width + 0.35f, 0.2f, 0.05f)
                : new Vector3(0.05f, 0.2f, width + 0.35f);
            Vector3 euler = alongX ? new Vector3(0f, 0f, tilt) : new Vector3(tilt, 0f, 0f);
            Prim(parent, name + "_Board" + i, PrimitiveType.Cube, pos, scale, plank, euler, false);
        }
    }

    // ------------------------------------------------------------ house lights
    /// <summary>
    /// The lamps the generator switches on. They are what makes protection visible:
    /// if the porch and hall are lit, the house is safe.
    /// </summary>
    static List<Light> BuildHouseLights(Transform house)
    {
        Transform t = Group(house, "Lights");
        List<Light> lights = new List<Light>();

        lights.Add(Lamp("Porch_Lamp", new Vector3(4.7f, 2.55f, -6.3f), 3.4f, 11f, new Color(1f, 0.72f, 0.40f)));

        // One lamp per room, ranged to reach that room's far corner - the living
        // room and kitchen are ~6 x 6, so 10 covers them diagonally. Nowhere
        // indoors should be flashlight-only while the generator runs.
        lights.Add(Lamp("Hall_LampS", new Vector3(0.3f, 2.65f, -2.6f), 2.6f, 9f, new Color(1f, 0.74f, 0.44f)));
        lights.Add(Lamp("Hall_LampN", new Vector3(0.3f, 2.65f, 2.8f), 2.6f, 9f, new Color(1f, 0.74f, 0.44f)));
        lights.Add(Lamp("Entrance_Lamp", new Vector3(4.3f, 2.65f, -3.6f), 3.0f, 10f, new Color(1f, 0.74f, 0.44f)));
        lights.Add(Lamp("Kitchen_Lamp", new Vector3(4.3f, 2.65f, 2.0f), 3.0f, 10f, new Color(1f, 0.76f, 0.48f)));
        lights.Add(Lamp("Living_Lamp", new Vector3(-4.0f, 2.65f, -2.6f), 3.0f, 10f, new Color(1f, 0.70f, 0.40f)));

        // The bedroom is now the dimmest room rather than the dead one, so it
        // still reads as the gloomy corner without being unplayable.
        lights.Add(Lamp("Bedroom_Lamp", new Vector3(-4.0f, 2.65f, 3.0f), 2.2f, 8f, new Color(1f, 0.68f, 0.38f)));

        return lights;

        Light Lamp(string name, Vector3 pos, float intensity, float range, Color colour)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(t, false);
            go.transform.localPosition = pos;

            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;

            // A visible bulb, so the source reads and not just the pool of light.
            Prim(go.transform, name + "_Bulb", PrimitiveType.Sphere, Vector3.zero,
                 new Vector3(0.14f, 0.14f, 0.14f), lampWarm, default(Vector3), false);
            Prim(go.transform, name + "_Flex", PrimitiveType.Cylinder, new Vector3(0f, 0.24f, 0f),
                 new Vector3(0.02f, 0.16f, 0.02f), metalDark, default(Vector3), false);
            return l;
        }
    }

    // -------------------------------------------------------------- furniture
    static void BuildFurniture(Transform house)
    {
        Transform f = Group(house, "Furniture");
        float y = FloorTop;

        // --- living room (west, south of the partition) -----------------------
        Transform living = Group(f, "LivingRoom");
        Box(living, "Rug", -6.2f, -2.0f, y, y + 0.02f, -4.8f, -1.4f, fabric, false);
        // Sofa, shoved out of place and facing the dead hearth.
        Transform sofa = Group(living, "Sofa");
        sofa.localPosition = new Vector3(-4.1f, 0f, -4.4f);
        sofa.localEulerAngles = new Vector3(0f, 8f, 0f);
        Box(sofa, "Sofa_Base", -1.1f, 1.1f, y, y + 0.42f, -0.45f, 0.45f, fabric);
        Box(sofa, "Sofa_Back", -1.1f, 1.1f, y + 0.42f, y + 0.95f, -0.45f, -0.25f, fabric);
        Box(sofa, "Sofa_ArmW", -1.15f, -0.9f, y + 0.42f, y + 0.72f, -0.45f, 0.45f, fabric);
        Box(sofa, "Sofa_ArmE", 0.9f, 1.15f, y + 0.42f, y + 0.72f, -0.45f, 0.45f, fabric);
        Box(sofa, "Sofa_Cushion", -0.95f, 0.95f, y + 0.42f, y + 0.55f, -0.28f, 0.4f, fabric, false);

        Table(living, "CoffeeTable", new Vector3(-4.1f, 0f, -2.9f), 1.3f, 0.7f, 0.42f, wood);
        // Bookshelf against the west wall, half its shelves collapsed.
        Transform shelf = Group(living, "Bookshelf");
        shelf.localPosition = new Vector3(-6.6f, 0f, -0.6f);
        Box(shelf, "Shelf_Body", -0.18f, 0.18f, y, y + 2.0f, -0.8f, 0.8f, wood);
        Box(shelf, "Shelf_Board1", -0.28f, 0.28f, y + 0.62f, y + 0.68f, -0.78f, 0.78f, plank, false);
        Box(shelf, "Shelf_Board2", -0.28f, 0.28f, y + 1.24f, y + 1.30f, -0.78f, 0.15f, plank, false);
        Prim(shelf, "Shelf_FallenBoard", PrimitiveType.Cube, new Vector3(0.5f, y + 0.03f, 0.5f),
             new Vector3(0.5f, 0.05f, 0.9f), plank, new Vector3(0f, 18f, 0f), false);

        // Fireplace under the chimney, long cold.
        Transform hearth = Group(living, "Fireplace");
        hearth.localPosition = new Vector3(-4.4f, 0f, -2.1f);
        Box(hearth, "Hearth_Base", -0.9f, 0.9f, y, y + 1.5f, -0.15f, 0.45f, rockMat);
        Box(hearth, "Hearth_Mouth", -0.45f, 0.45f, y, y + 0.85f, -0.2f, 0.2f, metalDark, false);
        Box(hearth, "Hearth_Mantle", -1.05f, 1.05f, y + 1.5f, y + 1.62f, -0.25f, 0.5f, wood);

        // A chair knocked onto its side. Small, but it sells "someone left in a hurry".
        Prim(living, "Chair_Overturned", PrimitiveType.Cube, new Vector3(-2.4f, y + 0.22f, -3.6f),
             new Vector3(0.45f, 0.45f, 0.06f), wood, new Vector3(0f, 40f, 82f));

        // --- kitchen (east, north of the partition) ---------------------------
        Transform kitchen = Group(f, "Kitchen");
        // Counter run along the north wall.
        Box(kitchen, "Counter_Body", 2.0f, 6.2f, y, y + 0.85f, 4.6f, 5.35f, wood);
        Box(kitchen, "Counter_Top", 1.9f, 6.3f, y + 0.85f, y + 0.92f, 4.5f, 5.4f, plank);
        Box(kitchen, "Sink_Basin", 2.6f, 3.5f, y + 0.62f, y + 0.86f, 4.7f, 5.25f, metalPale, false);
        Prim(kitchen, "Sink_Tap", PrimitiveType.Cylinder, new Vector3(3.05f, y + 1.08f, 5.25f),
             new Vector3(0.05f, 0.16f, 0.05f), metalPale, default(Vector3), false);
        // Stove.
        Box(kitchen, "Stove_Body", 4.4f, 5.4f, y, y + 0.88f, 4.6f, 5.35f, metalDark);
        for (int i = 0; i < 4; i++)
        {
            float bx = 4.6f + (i % 2) * 0.6f;
            float bz = 4.8f + (i / 2) * 0.4f;
            Prim(kitchen, "Stove_Burner" + i, PrimitiveType.Cylinder, new Vector3(bx, y + 0.90f, bz),
                 new Vector3(0.2f, 0.01f, 0.2f), metalPale, default(Vector3), false);
        }
        // Fridge, door hanging open on an empty interior.
        Transform fridge = Group(kitchen, "Fridge");
        fridge.localPosition = new Vector3(6.4f, 0f, 3.4f);
        Box(fridge, "Fridge_Body", -0.35f, 0.35f, y, y + 1.75f, -0.35f, 0.35f, metalPale);
        Prim(fridge, "Fridge_Door", PrimitiveType.Cube, new Vector3(-0.05f, y + 0.9f, -0.75f),
             new Vector3(0.72f, 1.7f, 0.07f), metalPale, new Vector3(0f, 62f, 0f));
        // Table and two chairs.
        Table(kitchen, "KitchenTable", new Vector3(3.9f, 0f, 1.6f), 1.6f, 0.95f, 0.76f, wood);
        BuildKeyMaker(kitchen.Find("KitchenTable"), y + 0.76f);
        Chair(kitchen, "Chair_A", new Vector3(3.9f, 0f, 0.75f), 0f);
        Chair(kitchen, "Chair_B", new Vector3(3.2f, 0f, 2.55f), 172f);
        Prim(kitchen, "Pot", PrimitiveType.Cylinder, new Vector3(4.2f, y + 0.86f, 1.5f),
             new Vector3(0.28f, 0.11f, 0.28f), metalDark, default(Vector3), false);
        Prim(kitchen, "Mug", PrimitiveType.Cylinder, new Vector3(3.45f, y + 0.82f, 1.9f),
             new Vector3(0.11f, 0.06f, 0.11f), ceramic, default(Vector3), false);

        // --- entrance / foyer (east, south of the partition) -------------------
        Transform foyer = Group(f, "Entrance");
        Box(foyer, "Mat", 3.6f, 5.0f, y, y + 0.02f, -5.1f, -4.1f, fabric, false);
        Box(foyer, "ShoeRack", 6.0f, 6.9f, y, y + 0.45f, -4.6f, -3.9f, wood);
        Box(foyer, "Crate", 2.1f, 2.8f, y, y + 0.6f, -2.6f, -1.9f, plank);
        Box(foyer, "Crate_Lid", 2.0f, 2.9f, y + 0.6f, y + 0.66f, -2.7f, -1.8f, plank, false);
        // Coat pegs by the door.
        for (int i = 0; i < 3; i++)
            // South of the archway (-4.2 .. -2.8), so no peg hangs in the opening.
            Prim(foyer, "Peg" + i, PrimitiveType.Cylinder, new Vector3(1.78f, y + 1.65f, -5.05f + i * 0.32f),
                 new Vector3(0.04f, 0.06f, 0.04f), metalDark, new Vector3(0f, 0f, 90f), false);

        // --- hallway ------------------------------------------------------------
        Transform hall = Group(f, "Hallway");
        Box(hall, "HallTable", -0.85f, -0.15f, y, y + 0.78f, 0.4f, 1.3f, wood);
        // A picture that fell off the wall, glass side down.
        Prim(hall, "FallenPicture", PrimitiveType.Cube, new Vector3(0.9f, y + 0.02f, -0.3f),
             new Vector3(0.5f, 0.04f, 0.4f), wood, new Vector3(0f, 27f, 0f), false);
        Prim(hall, "PictureHook", PrimitiveType.Cube, new Vector3(HallEastX - 0.07f, y + 2.1f, -0.3f),
             new Vector3(0.04f, 0.06f, 0.04f), metalDark, default(Vector3), false);

        // --- bedroom (west, north of the partition) - the extra room ------------
        Transform bed = Group(f, "Bedroom");
        Transform frame = Group(bed, "Bed");
        frame.localPosition = new Vector3(-5.4f, 0f, 3.4f);
        frame.localEulerAngles = new Vector3(0f, 90f, 0f);
        Box(frame, "Bed_Frame", -1.0f, 1.0f, y, y + 0.35f, -0.85f, 0.85f, wood);
        Box(frame, "Bed_Mattress", -0.95f, 0.95f, y + 0.35f, y + 0.60f, -0.8f, 0.8f, fabric);
        Box(frame, "Bed_Pillow", -0.9f, -0.45f, y + 0.60f, y + 0.72f, -0.45f, 0.45f, fabric, false);
        Box(frame, "Bed_Head", -1.05f, -0.95f, y + 0.35f, y + 1.15f, -0.85f, 0.85f, wood);
        Box(bed, "Nightstand", -6.7f, -6.1f, y, y + 0.55f, 1.7f, 2.3f, wood);
        Prim(bed, "Candle", PrimitiveType.Cylinder, new Vector3(-6.4f, y + 0.63f, 2.0f),
             new Vector3(0.07f, 0.08f, 0.07f), ceramic, default(Vector3), false);
        // Wardrobe, one door ajar.
        Transform ward = Group(bed, "Wardrobe");
        ward.localPosition = new Vector3(-2.0f, 0f, 4.6f);
        Box(ward, "Wardrobe_Body", -0.55f, 0.55f, y, y + 2.05f, -0.32f, 0.32f, wood);
        Box(ward, "Wardrobe_DoorL", -0.55f, -0.02f, y + 0.05f, y + 2.0f, -0.4f, -0.32f, plank, false);
        Prim(ward, "Wardrobe_DoorR", PrimitiveType.Cube, new Vector3(0.5f, y + 1.0f, -0.62f),
             new Vector3(0.53f, 1.95f, 0.07f), plank, new Vector3(0f, 48f, 0f));
        // Child's toy left behind - the one prop that does the emotional work.
        Prim(bed, "Toy", PrimitiveType.Sphere, new Vector3(-3.4f, y + 0.13f, 2.2f),
             new Vector3(0.26f, 0.26f, 0.26f), fabric, default(Vector3), false);

        // --- helpers ------------------------------------------------------------
        void Table(Transform parent, string name, Vector3 at, float w, float d, float h, Material m)
        {
            Transform t = Group(parent, name);
            t.localPosition = at;
            Box(t, name + "_Top", -w * 0.5f, w * 0.5f, y + h - 0.06f, y + h, -d * 0.5f, d * 0.5f, m);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (w * 0.5f - 0.09f);
                float lz = (i / 2 == 0 ? -1f : 1f) * (d * 0.5f - 0.09f);
                Prim(t, name + "_Leg" + i, PrimitiveType.Cube, new Vector3(lx, y + (h - 0.06f) * 0.5f, lz),
                     new Vector3(0.07f, h - 0.06f, 0.07f), m, default(Vector3), false);
            }
        }

        void Chair(Transform parent, string name, Vector3 at, float yaw)
        {
            Transform t = Group(parent, name);
            t.localPosition = at;
            t.localEulerAngles = new Vector3(0f, yaw, 0f);
            Box(t, name + "_Seat", -0.22f, 0.22f, y + 0.42f, y + 0.48f, -0.22f, 0.22f, wood);
            Box(t, name + "_Back", -0.22f, 0.22f, y + 0.48f, y + 0.92f, -0.24f, -0.18f, wood);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * 0.18f;
                float lz = (i / 2 == 0 ? -1f : 1f) * 0.18f;
                Prim(t, name + "_Leg" + i, PrimitiveType.Cube, new Vector3(lx, y + 0.21f, lz),
                     new Vector3(0.05f, 0.42f, 0.05f), wood, default(Vector3), false);
            }
        }
    }

    // ---------------------------------------------------------------- forest
    static bool Blocked(Vector2 p, float clearance)
    {
        // Keep the house, its porch and the approach path clear.
        if (Mathf.Abs(p.x) < HouseHalfX + 3.5f && Mathf.Abs(p.y) < HouseHalfZ + 3.5f) return true;
        if (p.x > 0.5f && p.x < 11.5f && p.y > -11.5f && p.y < -5f) return true;             // porch + generator
        if (p.x > 2.4f && p.x < 7.0f && p.y > -22f && p.y < -5f) return true;                // path
        if (Vector2.Distance(p, new Vector2(PlayerSpawn.x, PlayerSpawn.z)) < 4f) return true;

        // Nothing grows on the mountain or hard against its face: the rock is placed as one
        // mass afterwards, and a trunk standing inside it would be a tree in a wall. The apron
        // in front of the mouth is kept clear too, so the one way in is something you can see
        // from a few strides away rather than a slot behind a pine.
        if (MountainS(p) > MountainFront - MountainClear) return true;
        if (Vector2.Distance(p, new Vector2(CaveMouth.x, CaveMouth.z)) < 10f) return true;


        // The tracks are cleared before a single tree is placed. Doing it the other way
        // round -- growing the forest and then carving -- leaves trunks standing in the
        // path, because Scatter has already committed.
        foreach (Vector2[] trail in Trails)
            for (int i = 0; i < trail.Length - 1; i++)
                if (DistanceToSegment(p, trail[i], trail[i + 1]) < TrailClear) return true;

        foreach (Vector2 o in occupied)
            if (Vector2.SqrMagnitude(o - p) < clearance * clearance) return true;
        return false;
    }

    /// <summary>Shortest distance from a point to a line segment, in the ground plane.</summary>
    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 0.0001f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// Scatter over the SQUARE out to <paramref name="halfExtent"/>, skipping anything inside
    /// <paramref name="rMin"/>. The polar <see cref="Scatter"/> can only ever fill a disc, which
    /// on a square map leaves the four corners as bare grass -- about a quarter of a 200 m
    /// level. This is what closes them.
    /// </summary>
    static bool ScatterSquare(float rMin, float halfExtent, float clearance, out Vector2 result)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Vector2 p = new Vector2(Random.Range(-halfExtent, halfExtent), Random.Range(-halfExtent, halfExtent));
            if (p.sqrMagnitude < rMin * rMin) continue;
            if (Blocked(p, clearance)) continue;
            result = p;
            return true;
        }
        result = Vector2.zero;
        return false;
    }

    static bool Scatter(float rMin, float rMax, float clearance, out Vector2 result)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.Range(rMin * rMin, rMax * rMax));  // even area coverage
            Vector2 p = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            if (Mathf.Abs(p.x) > GroundHalf - 3f || Mathf.Abs(p.y) > GroundHalf - 3f) continue;
            if (Blocked(p, clearance)) continue;
            result = p;
            return true;
        }
        result = Vector2.zero;
        return false;
    }

    static void BuildForest()
    {
        Transform forest = Root("Forest");
        Transform pines = Group(forest, "Pines");
        Transform broadleaf = Group(forest, "Broadleaf");
        Transform dead = Group(forest, "DeadTrees");
        Transform bushes = Group(forest, "Bushes");
        Transform rocks = Group(forest, "Rocks");

        int n = 0;
        // Near stand, around the house. The densest woods in the level: this is the band
        // that has to read as "you cannot see past this" from the porch.
        for (int i = 0; i < 300; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner, ForestNear, 2.9f, out p)) continue;
            occupied.Add(p);
            if (Random.value < 0.72f) Pine(pines, "Pine_" + (n++), p, Random.Range(4.2f, 7.4f));
            else Broadleaf(broadleaf, "Tree_" + (n++), p, Random.Range(3.8f, 5.6f));
        }

        // Mid band: thinner per square metre but far larger, so it is still a long walk
        // through trees. Spacing is wider, which is also what keeps the bake affordable.
        for (int i = 0; i < 420; i++)
        {
            Vector2 p;
            if (!Scatter(ForestNear, ForestMid, 3.6f, out p)) continue;
            occupied.Add(p);
            if (Random.value < 0.78f) Pine(pines, "Pine_Mid_" + (n++), p, Random.Range(4.6f, 8.0f));
            else Broadleaf(broadleaf, "Tree_Mid_" + (n++), p, Random.Range(4.0f, 6.0f));
        }

        // The outer band, filled over the SQUARE so the corners are woods too. A disc leaves
        // roughly a quarter of a 200 m map as open grass, which reads as the edge of the world.
        for (int i = 0; i < 340; i++)
        {
            Vector2 p;
            if (!ScatterSquare(ForestMid, GroundHalf - ForestEdge, 2.6f, out p)) continue;
            occupied.Add(p);
            Pine(pines, "Pine_Edge_" + (n++), p, Random.Range(5.0f, 8.0f));
        }

        for (int i = 0; i < 55; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner - 2f, ForestMid, 2.6f, out p)) continue;
            occupied.Add(p);
            DeadTree(dead, "Dead_" + i, p, Random.Range(3.0f, 5.5f));
        }

        // Undergrowth over the square too, for the same corner reason.
        for (int i = 0; i < 300; i++)
        {
            Vector2 p;
            if (!ScatterSquare(ForestInner - 4f, GroundHalf - 5f, 1.4f, out p)) continue;
            Bush(bushes, "Bush_" + i, p);
        }

        for (int i = 0; i < 180; i++)
        {
            Vector2 p;
            if (!ScatterSquare(ForestInner - 5f, GroundHalf - 5f, 1.6f, out p)) continue;
            Rock(rocks, "Rock_" + i, p);
        }
    }

    /// <summary>
    /// The dirt strips the trails are actually made of. Render-only quads laid segment by
    /// segment, like Ground_Path -- they are navigation, not collision, and the forest was
    /// already kept off them by <see cref="Blocked"/> before a tree was placed.
    /// </summary>
    static void BuildTrails(Transform ground)
    {
        Transform t = Group(ground, "Trails");

        for (int r = 0; r < Trails.Length; r++)
        {
            Vector2[] route = Trails[r];
            for (int i = 0; i < route.Length - 1; i++)
            {
                Vector2 a = route[i], b = route[i + 1];
                Vector2 mid = (a + b) * 0.5f;
                float length = Vector2.Distance(a, b);
                float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;

                // A hair above the ground and a hair below the house floors, same as the path.
                Prim(t, "Trail_" + r + "_" + i, PrimitiveType.Cube,
                     new Vector3(mid.x, GroundTop + 0.01f, mid.y),
                     new Vector3(TrailHalfWidth * 2f, 0.02f, length + TrailHalfWidth),
                     dirt, new Vector3(0f, yaw, 0f), false);
            }
        }
    }

    /// <summary>Low-poly conifer: a trunk with four tapering cylinder tiers.</summary>
    static void Pine(Transform parent, string name, Vector2 at, float h)
    {
        Transform t = Group(parent, name);
        t.localPosition = new Vector3(at.x, GroundTop, at.y);
        t.localEulerAngles = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(0f, 360f), Random.Range(-2.5f, 2.5f));

        Material leaf = Random.value < 0.5f ? foliage : foliageAlt;
        // Only the trunk collides: the canopy sits above head height anyway, and
        // 150 extra capsule colliders would cost more than they are worth.
        Prim(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, h * 0.35f, 0f), new Vector3(0.22f, h * 0.35f, 0.22f), bark);
        Prim(t, "Tier0", PrimitiveType.Cylinder, new Vector3(0f, h * 0.42f, 0f), new Vector3(h * 0.60f, h * 0.09f, h * 0.60f), leaf, default(Vector3), false);
        Prim(t, "Tier1", PrimitiveType.Cylinder, new Vector3(0f, h * 0.62f, 0f), new Vector3(h * 0.47f, h * 0.08f, h * 0.47f), leaf, default(Vector3), false);
        Prim(t, "Tier2", PrimitiveType.Cylinder, new Vector3(0f, h * 0.80f, 0f), new Vector3(h * 0.32f, h * 0.07f, h * 0.32f), leaf, default(Vector3), false);
        Prim(t, "Tip", PrimitiveType.Cylinder, new Vector3(0f, h * 0.94f, 0f), new Vector3(h * 0.15f, h * 0.06f, h * 0.15f), leaf, default(Vector3), false);
    }

    /// <summary>Low-poly broadleaf: a trunk under two offset canopy spheres.</summary>
    static void Broadleaf(Transform parent, string name, Vector2 at, float h)
    {
        Transform t = Group(parent, name);
        t.localPosition = new Vector3(at.x, GroundTop, at.y);
        t.localEulerAngles = new Vector3(0f, Random.Range(0f, 360f), 0f);

        Material leaf = Random.value < 0.5f ? foliage : foliageAlt;
        Prim(t, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, h * 0.32f, 0f), new Vector3(0.26f, h * 0.32f, 0.26f), bark);
        Prim(t, "Canopy", PrimitiveType.Sphere, new Vector3(0f, h * 0.82f, 0f), new Vector3(h * 0.80f, h * 0.62f, h * 0.80f), leaf, default(Vector3), false);
        Prim(t, "Canopy_B", PrimitiveType.Sphere, new Vector3(h * 0.16f, h * 0.66f, h * 0.12f), new Vector3(h * 0.52f, h * 0.42f, h * 0.52f), leaf, default(Vector3), false);
    }

    /// <summary>A bare leaning snag - the silhouettes that read as "wrong" at night.</summary>
    static void DeadTree(Transform parent, string name, Vector2 at, float h)
    {
        Transform t = Group(parent, name);
        t.localPosition = new Vector3(at.x, GroundTop, at.y);
        t.localEulerAngles = new Vector3(Random.Range(-11f, 11f), Random.Range(0f, 360f), Random.Range(-11f, 11f));

        Prim(t, "Snag", PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.24f, h * 0.5f, 0.24f), bark);
        Prim(t, "Branch_A", PrimitiveType.Cylinder, new Vector3(0.45f, h * 0.78f, 0f), new Vector3(0.08f, 0.55f, 0.08f), bark, new Vector3(0f, 0f, -58f), false);
        Prim(t, "Branch_B", PrimitiveType.Cylinder, new Vector3(-0.38f, h * 0.62f, 0.2f), new Vector3(0.07f, 0.45f, 0.07f), bark, new Vector3(20f, 0f, 52f), false);
    }

    static void Bush(Transform parent, string name, Vector2 at)
    {
        Transform t = Group(parent, name);
        t.localPosition = new Vector3(at.x, GroundTop, at.y);
        t.localEulerAngles = new Vector3(0f, Random.Range(0f, 360f), 0f);

        float s = Random.Range(0.8f, 1.7f);
        Prim(t, "Mass", PrimitiveType.Sphere, new Vector3(0f, s * 0.34f, 0f), new Vector3(s * 1.3f, s * 0.75f, s * 1.3f), bushMat, default(Vector3), false);
        Prim(t, "Mass_B", PrimitiveType.Sphere, new Vector3(s * 0.4f, s * 0.26f, s * 0.25f), new Vector3(s * 0.9f, s * 0.6f, s * 0.9f), bushMat, default(Vector3), false);
    }

    static void Rock(Transform parent, string name, Vector2 at)
    {
        float s = Random.Range(0.4f, 1.8f);
        // Sunk into the ground so it reads as bedrock, not a dropped ball.
        Prim(parent, name, PrimitiveType.Sphere,
             new Vector3(at.x, GroundTop + s * 0.18f, at.y),
             new Vector3(s, s * 0.7f, s * Random.Range(0.7f, 1.2f)), rockMat,
             new Vector3(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f)),
             s > 0.9f);
    }

    // -------------------------------------------------------------- the mountain
    /// <summary>
    /// The rock that replaced the south-east quarter of the forest.
    ///
    /// **It is one mass with one hole in it.** The wedge is filled with a grid of
    /// interlocking blocks, rising from the face towards the corner, and every block whose
    /// centre falls inside a carve from <see cref="CaveRuns"/> is simply not built from the
    /// ground up -- it starts at <see cref="CaveCeiling"/> instead, so the same box that would
    /// have been solid rock becomes the roof over a passage. That is the whole trick: there is
    /// no separate cave shell to keep aligned with a separate mountain, and a void can never
    /// end up with a hole in its ceiling or a gap to the sky.
    ///
    /// Blocks overlap by <see cref="MountainOverlap"/> and the jitter is kept under half of
    /// that, so neighbours always intersect -- otherwise a player would eventually find the
    /// one seam they could walk through, and "inaccessible except by the mouth" would be a
    /// claim rather than a fact.
    ///
    /// What the run then hangs on it, in order: the floor, the dark, the cut-out that keeps
    /// the current monsters out until this place has one of its own, the dead man's camp and
    /// his note, the marks only ultraviolet shows, and the wall at the end of them.
    /// </summary>
    static void BuildMountain()
    {
        Transform root = Root("Mountain");

        // The rock is an obstacle, never a floor. Its blocks are big flat-topped boxes, so
        // without this the bake covers the whole mountain in walkable islands twenty metres
        // up -- measured, 14% of spawn candidates were landing on the roof of the level and
        // standing there all night. Marking it Not Walkable leaves it solid to the voxeliser
        // and out of the mesh.
        //
        // It does NOT make the cave off-mesh: the cave's floor is the ground cube, which is not
        // a child of this. That is what the CaveOffMesh volumes are for, and the two jobs are
        // separate on purpose.
        NavMeshModifier obstacle = root.gameObject.AddComponent<NavMeshModifier>();
        obstacle.overrideArea = true;
        obstacle.area = 1;                       // Not Walkable
        obstacle.applyToChildren = true;

        Transform mass = Group(root, "Mass");
        Transform roofs = Group(root, "Roof");
        Transform floors = Group(root, "Floor");
        Transform scree = Group(root, "Scree");

        // Blocks are laid out on the corner axis, so they stand square to the rock face
        // rather than to the world.
        const float yaw = 135f;
        int solid = 0, voids = 0;

        for (float s = MountainFront + MountainCell * 0.5f; s < MountainReach; s += MountainCell)
        {
            // The map's own edges, in this frame. Overrun them by a block: a mass that stopped
            // exactly on the boundary would leave a gap between the last block and the
            // boundary wall wide enough to walk down -- straight round the outside of the
            // mountain and into the far end of it, which is the one thing it must not allow.
            float halfWidth = MountainReach - s + MountainCell;

            for (float t = -halfWidth; t <= halfWidth; t += MountainCell)
            {
                float js = s + Random.Range(-0.6f, 0.6f);
                float jt = t + Random.Range(-0.6f, 0.6f);

                Vector3 at = Mountain(js, jt);
                if (Mathf.Abs(at.x) > GroundHalf + MountainCell ||
                    Mathf.Abs(at.z) > GroundHalf + MountainCell) continue;

                // Taller the further in, so the mass reads as a mountain from the treeline
                // rather than as a wall. Two long waves across it on top of that, because a
                // clean ramp of blocks reads as a quarry: the ridges and hollows they put in
                // are what make the skyline look weathered rather than cut.
                float height = Mathf.Lerp(MountainBaseHeight, MountainPeakHeight,
                                          (js - MountainFront) / (MountainReach - MountainFront))
                               + Mathf.Sin(js * 0.085f) * 5.5f + Mathf.Cos(jt * 0.115f + 1.3f) * 4.5f
                               + Random.Range(-2.5f, 2.5f);

                height = Mathf.Max(height, CaveCeiling + 3f);   // never thinner than the cave's roof

                bool hollow = InCave(js, jt, 0f);
                float bottom = hollow ? CaveCeiling : -2f;
                if (hollow) voids++; else solid++;

                Prim(hollow ? roofs : mass, (hollow ? "Roof_" : "Rock_") + solid + "_" + voids,
                     PrimitiveType.Cube,
                     new Vector3(at.x, (bottom + height) * 0.5f, at.z),
                     new Vector3(MountainCell + MountainOverlap, height - bottom, MountainCell + MountainOverlap),
                     rockMat, new Vector3(0f, yaw + Random.Range(-4f, 4f), 0f));

                if (hollow)
                {
                    // Gravel underfoot, render-only: the ground cube is still what you walk on.
                    Prim(floors, "CaveFloor_" + voids, PrimitiveType.Cube,
                         new Vector3(at.x, GroundTop + 0.01f, at.z),
                         new Vector3(MountainCell + MountainOverlap, 0.02f, MountainCell + MountainOverlap),
                         caveFloor, new Vector3(0f, yaw, 0f), false);
                }
                else if (js < MountainFront + MountainCell * 2.5f || Random.value < 0.45f)
                {
                    // Boulders on the shoulders, so the silhouette is not a staircase of cubes.
                    float r = Random.Range(2.5f, 6.5f);
                    Prim(scree, "Boulder_" + solid, PrimitiveType.Sphere,
                         new Vector3(at.x + Random.Range(-2f, 2f), height - r * 0.35f, at.z + Random.Range(-2f, 2f)),
                         new Vector3(r, r * Random.Range(0.5f, 0.8f), r * Random.Range(0.8f, 1.2f)), rockMat,
                         new Vector3(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f)), false);
                }
            }
        }

        BuildMountainSkirt(scree);
        BuildCaveVolumes(root);
        BuildCaveMouth(root);
        BuildCaveCamp(root);
        BuildCaveTrail(root);
        BuildHiddenDoor(root);

        Debug.Log(string.Format("Mountain built: {0} blocks of rock, {1} of them hanging over the cave.",
                                solid, voids));
    }

    /// <summary>
    /// Fallen rock heaped along the foot of the face, render-only. Without it the mountain
    /// meets the grass on a dead straight line and reads as scenery rather than as ground.
    /// </summary>
    static void BuildMountainSkirt(Transform parent)
    {
        for (int i = 0; i < 120; i++)
        {
            float t = Random.Range(-(MountainReach - MountainFront), MountainReach - MountainFront);
            float s = MountainFront + Random.Range(-4.5f, 1.5f);

            Vector3 at = Mountain(s, t);
            if (Mathf.Abs(at.x) > GroundHalf - 1f || Mathf.Abs(at.z) > GroundHalf - 1f) continue;
            if (Vector2.Distance(new Vector2(at.x, at.z), new Vector2(CaveMouth.x, CaveMouth.z)) < 7f) continue;

            float r = Random.Range(0.8f, 3.4f);
            Prim(parent, "Scree_" + i, PrimitiveType.Sphere,
                 new Vector3(at.x, GroundTop + r * 0.2f, at.z),
                 new Vector3(r, r * 0.7f, r * Random.Range(0.7f, 1.3f)), rockMat,
                 new Vector3(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)),
                 r > 2.2f);
        }
    }

    /// <summary>
    /// One box per passage, twice over: the volume <see cref="MountainInterior"/> reads to know
    /// the sky is gone, and a NavMesh cut-out over the same box.
    ///
    /// **The cut-out is deliberate and is the seam the mountain's own monster arrives on.** The
    /// wave in the woods has no idea this place exists, and a blind hunter wandering into a
    /// pitch-black cave the player is feeling their way through is a balance decision, not a
    /// side effect of geometry. Delete these volumes -- or give the mountain monster its own
    /// agent type -- and the cave joins the walkable world in one bake.
    /// </summary>
    static void BuildCaveVolumes(Transform root)
    {
        Transform dark = Group(root, "CaveDark");
        Transform offMesh = Group(root, "CaveOffMesh");

        List<Transform> volumes = new List<Transform>();
        int n = 0;

        foreach (Vector3[] run in CaveRuns)
        {
            for (int i = 0; i < run.Length - 1; i++, n++)
            {
                Vector3 a = Mountain(run[i].x, run[i].y);
                Vector3 b = Mountain(run[i + 1].x, run[i + 1].y);

                float width = (Mathf.Max(run[i].z, run[i + 1].z) + 1.5f) * 2f;

                // Overrun each end by the fade distance, so consecutive volumes overlap by
                // twice it and every corner between two passages is deep inside at least one
                // of them. Without it the fade on the length axis meets another fade at the
                // joint and every bend in the cave is a patch of half-light -- measured at 0.32
                // where it should be 1. The first run starts on the face of the rock, so the
                // only overrun that shows is the one at the mouth, which is the fade wanted.
                float length = Vector3.Distance(a, b) + CaveDarkSoftness * 2f;
                float heading = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;

                Vector3 centre = (a + b) * 0.5f;
                centre.y = CaveCeiling * 0.5f;

                GameObject box = new GameObject("CaveVolume_" + n);
                box.transform.SetParent(dark, false);
                box.transform.localPosition = centre;
                box.transform.localEulerAngles = new Vector3(0f, heading, 0f);
                box.transform.localScale = new Vector3(width, CaveCeiling + 1f, length);
                volumes.Add(box.transform);

                // Same box, unscaled, so the modifier's own size is the size it says it is.
                GameObject cut = new GameObject("CaveOffMesh_" + n);
                cut.transform.SetParent(offMesh, false);
                cut.transform.localPosition = centre;
                cut.transform.localEulerAngles = new Vector3(0f, heading, 0f);

                NavMeshModifierVolume volume = cut.AddComponent<NavMeshModifierVolume>();
                volume.size = new Vector3(width, CaveCeiling + 4f, length);
                volume.center = Vector3.zero;
                volume.area = 1;                        // Not Walkable
            }
        }

        // On the mountain rather than on Systems: it owns no roll and no shared state, only an
        // answer about where the rock is, so it belongs with the rock and is rebuilt with it.
        MountainInterior interior = root.gameObject.AddComponent<MountainInterior>();
        interior.volumes = volumes.ToArray();
        interior.softness = CaveDarkSoftness;         // the same number the overlap above is built from
    }

    /// <summary>The mouth itself: a lintel and two shoulders, so the one way in reads as a way in.</summary>
    static void BuildCaveMouth(Transform root)
    {
        Transform mouth = Group(root, "Mouth");
        Vector3 at = CaveMouth;

        // Square to the face, like everything else on this side of the map.
        mouth.localPosition = new Vector3(at.x, 0f, at.z);
        mouth.localEulerAngles = new Vector3(0f, 135f, 0f);

        Box(mouth, "Mouth_Lintel", -5.5f, 5.5f, CaveCeiling - 1.4f, CaveCeiling + 1.2f, -1.2f, 1.2f, rockMat);

        for (int i = 0; i < 9; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float r = Random.Range(1.6f, 3.6f);
            Prim(mouth, "Mouth_Rock_" + i, PrimitiveType.Sphere,
                 new Vector3(side * Random.Range(3.4f, 6.5f), GroundTop + r * 0.25f, Random.Range(-2.5f, 2.5f)),
                 new Vector3(r, r * 0.9f, r), rockMat,
                 new Vector3(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f)), false);
        }
    }

    /// <summary>
    /// What is left of whoever was down here last, and the one piece of writing in the place.
    ///
    /// The note is the whole of the puzzle's teaching and it teaches nothing directly: it is a
    /// man's account of what he saw, and every mechanic in it is described by its effect and
    /// never by its name. Working out that "the violet lamp" is something the store sells, and
    /// that "a handful of anything fine enough" is the jar, is the player's job -- Nina's
    /// drawing in the shed is the other half of it, and neither says what to do.
    /// </summary>
    static void BuildCaveCamp(Transform root)
    {
        Transform camp = Group(root, "Camp");
        Vector3 at = Mountain(72.5f, 8.5f);
        camp.localPosition = new Vector3(at.x, 0f, at.z);
        camp.localEulerAngles = new Vector3(0f, 135f, 0f);

        float g = GroundTop;

        Box(camp, "Camp_Crate", -0.45f, 0.45f, g, g + 0.62f, -0.4f, 0.4f, plank);
        Box(camp, "Camp_Bedroll", -1.9f, -0.7f, g, g + 0.18f, -0.9f, 0.9f, fabric, false);
        Prim(camp, "Camp_Lamp", PrimitiveType.Sphere, new Vector3(0f, g + 0.74f, 0.2f),
             new Vector3(0.2f, 0.24f, 0.2f), lampDead, default(Vector3), false);
        Prim(camp, "Camp_Lamp_Base", PrimitiveType.Cylinder, new Vector3(0f, g + 0.66f, 0.2f),
             new Vector3(0.18f, 0.05f, 0.18f), metalDark, default(Vector3), false);

        for (int i = 0; i < 5; i++)
        {
            Prim(camp, "Camp_Ash_" + i, PrimitiveType.Cylinder,
                 new Vector3(Random.Range(0.8f, 1.9f), g + 0.02f, Random.Range(-0.8f, 0.8f)),
                 new Vector3(Random.Range(0.3f, 0.7f), 0.01f, Random.Range(0.3f, 0.7f)), lampDead,
                 default(Vector3), false);
        }

        GameObject page = Prim(camp, "Camp_Note", PrimitiveType.Cube,
                               new Vector3(-0.05f, g + 0.64f, -0.1f), new Vector3(0.24f, 0.01f, 0.3f),
                               paint, new Vector3(0f, 18f, 0f));

        Readable note = page.AddComponent<Readable>();
        note.prompt = "Read the page";
        note.title = "A page torn from a notebook";
        note.text =
            "Third time down. Halvard would not come past the first bend and I have stopped " +
            "asking him to.\n\n" +
            "I took the violet lamp in with me. He laughed at me for buying it and he can go " +
            "on laughing. Under it the floor is not empty. There are marks on it. Steps, I " +
            "think, small ones, going inward -- and none of them coming back out. They are " +
            "there while the lamp is on them and they are gone the moment it is not, and I " +
            "have given up trying to explain that to myself.\n\n" +
            "I followed them as far as my nerve held. They stop at the back of the far " +
            "gallery, where the rock is plain and flat and there is nothing there at all. " +
            "Nothing. And yet the air moves against my hand when I hold it up to the stone, " +
            "and it moves from somewhere.\n\n" +
            "Halvard says a handful of anything fine enough would settle it. Chalk. Flour. " +
            "Ash off the fire. Throw it at a thing you cannot see, he says, and it will hold " +
            "the shape of it for you. I had nothing fine enough left in my pack and I was not " +
            "going back down a fourth time to find out.\n\n" +
            "Going up for air. If I do not write again it is because I went back down.";
    }

    /// <summary>
    /// The marks. Bare feet, small, going inward -- painted in the same ultraviolet as the
    /// trail outside, so a player who has met one already knows what they are looking at.
    ///
    /// They lead from the camp to the back wall of the far gallery and stop there. Nothing
    /// marks the wall itself: the marks running out at a blank face IS the clue, and a cross
    /// daubed on the stone would do the powder's job for it.
    /// </summary>
    static void BuildCaveTrail(Transform root)
    {
        Transform trail = Group(root, "UVTrail");

        const float spacing = 2.4f;
        float carried = 0f;
        int n = 0;

        for (int i = 0; i < CaveTrail.Length - 1; i++)
        {
            Vector2 a = CaveTrail[i], b = CaveTrail[i + 1];
            float length = Vector2.Distance(a, b);
            if (length < 0.01f) continue;

            for (float d = carried; d < length; d += spacing, n++)
            {
                Vector2 st = Vector2.Lerp(a, b, d / length);
                Vector3 at = Mountain(st.x, st.y);

                Vector2 step = (b - a) / length;
                Vector3 ahead = Mountain(st.x + step.x, st.y + step.y) - at;

                Transform foot = Group(trail, "Step_" + n);
                foot.localPosition = new Vector3(at.x, GroundTop + 0.02f, at.z);
                foot.localEulerAngles = new Vector3(0f,
                    Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg + Random.Range(-10f, 10f), 0f);

                // Left, right, left: a walk, not a dotted line.
                float side = (n % 2 == 0 ? -0.24f : 0.24f);
                Prim(foot, "Sole", PrimitiveType.Cube, new Vector3(side, 0f, 0.04f),
                     new Vector3(0.09f, 0.012f, 0.17f), paint, default(Vector3), false);
                Prim(foot, "Heel", PrimitiveType.Cube, new Vector3(side, 0f, -0.09f),
                     new Vector3(0.075f, 0.012f, 0.07f), paint, default(Vector3), false);

                foot.gameObject.AddComponent<UVRevealed>();
            }

            carried = (carried - length) % spacing;
            if (carried < 0f) carried += spacing;
        }
    }

    /// <summary>
    /// The back wall of the far gallery, and the fact that it is not a wall.
    ///
    /// While it is concealed, what stands in the opening is <c>Door_Plug</c>: a slab of the
    /// same rock as the gallery, flush with the wall around it and solid. That is the point --
    /// hiding the door by switching its renderers off would leave a corridor running on into
    /// the dark for anyone to see, and switching its colliders off would let them walk through
    /// the mountain. <see cref="Concealed"/> swaps the two over when the powder lands.
    ///
    /// The surround is never swapped: it is ordinary rock with a door-shaped hole in it, so
    /// the moment the plug goes there is a doorway rather than a ragged gap.
    /// </summary>
    static void BuildHiddenDoor(Transform root)
    {
        Transform site = Group(root, "HiddenDoor");

        Vector3 at = Mountain(MountainDoorS, MountainDoorT);

        // Facing along the passage it blocks, so the leaf swings back into the gallery.
        Vector3 along = Mountain(120f, -8f) - Mountain(110f, -16f);
        site.localPosition = new Vector3(at.x, 0f, at.z);
        site.localEulerAngles = new Vector3(0f, Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg, 0f);

        const float gapHalf = 1.25f;
        const float gapTop = 2.65f;

        // Rock with a hole in it, and the hole is the only way past it.
        //
        // It reaches 14 m to either side, which is far more than the passage is wide -- and
        // that width is the whole point rather than sloppiness. The far gallery is a carved
        // blob, not a tube, so the open floor at this plane is several metres wider than the
        // passage that leaves it; a wall that only spanned the passage would leave floor to
        // walk round its ends, and the vault would be reachable with no powder at all. It was,
        // the first time. Everything past the doorway is buried in the mass, so the cost of
        // being generous here is nothing.
        //
        // It is deliberately a SIBLING of the door rather than a child of it. The powder finds
        // what it has landed near by looking up the hierarchy from whatever it touched, so a
        // fourteen-metre wall parented under the door would make every inch of the back of the
        // gallery a place to find it. Out here the wall is only rock, and the door is found by
        // pouring at the door.
        Transform surround = Group(root, "DoorWall");
        surround.localPosition = site.localPosition;
        surround.localRotation = site.localRotation;
        Box(surround, "Wall_W", -14f, -gapHalf, -2f, CaveCeiling + 2f, -0.6f, 0.6f, rockMat);
        Box(surround, "Wall_E", gapHalf, 14f, -2f, CaveCeiling + 2f, -0.6f, 0.6f, rockMat);
        Box(surround, "Wall_Top", -gapHalf, gapHalf, gapTop, CaveCeiling + 2f, -0.6f, 0.6f, rockMat);

        // The lie.
        GameObject plug = Box(site, "Door_Plug", -gapHalf - 0.05f, gapHalf + 0.05f, GroundTop, gapTop + 0.05f,
                              -0.4f, 0.4f, rockMat);

        // What is really there.
        Transform frame = Group(site, "Frame");
        Box(frame, "Jamb_W", -gapHalf, -gapHalf + 0.16f, GroundTop, gapTop, -0.2f, 0.2f, metalDark);
        Box(frame, "Jamb_E", gapHalf - 0.16f, gapHalf, GroundTop, gapTop, -0.2f, 0.2f, metalDark);
        Box(frame, "Lintel", -gapHalf, gapHalf, gapTop - 0.16f, gapTop, -0.2f, 0.2f, metalDark);

        Transform hinge = Group(site, "Door_Hinge");
        hinge.localPosition = new Vector3(-gapHalf + 0.16f, 0f, 0f);
        Box(hinge, "Door_Leaf", 0.02f, 2.2f, GroundTop, gapTop - 0.18f, -0.07f, 0.07f, wood);
        Box(hinge, "Door_Band", 0.02f, 2.2f, 1.6f, 1.78f, -0.09f, 0.09f, metalDark);
        Box(hinge, "Door_Handle", 1.86f, 2.06f, 1.15f, 1.35f, -0.13f, 0.13f, metalPale);

        DoorInteraction door = hinge.gameObject.AddComponent<DoorInteraction>();
        door.openAngle = 96f;
        door.canBeForced = false;                    // nothing on this side to force it

        // What the powder's overlap search actually hits. A trigger, and never switched off:
        // a thing that cannot be found cannot be revealed. Reaches out into the gallery so the
        // dust does not have to land on the exact centimetre of stone.
        GameObject probe = new GameObject("Door_Volume");
        probe.transform.SetParent(site, false);
        probe.transform.localPosition = new Vector3(0f, 1.4f, -1.2f);
        BoxCollider probeBox = probe.AddComponent<BoxCollider>();
        probeBox.size = new Vector3(3.6f, 2.8f, 2.6f);
        probeBox.isTrigger = true;

        Concealed concealed = site.gameObject.AddComponent<Concealed>();
        concealed.probeVolume = probeBox;
        concealed.disguise = plug.GetComponentsInChildren<Renderer>();
        concealed.disguiseSolids = plug.GetComponentsInChildren<Collider>();
        concealed.dormant = new Behaviour[] { door };

        List<Renderer> hidden = new List<Renderer>();
        hidden.AddRange(frame.GetComponentsInChildren<Renderer>());
        hidden.AddRange(hinge.GetComponentsInChildren<Renderer>());
        concealed.hidden = hidden.ToArray();

        // The door's own colliders go with it, and the doorway stays solid anyway because the
        // plug is what is filling it. Leaving them live would put an interactable with a live
        // collider inside a wall the player is not supposed to know about: the plug happens to
        // block the line-of-sight check today, but a prompt that only stays hidden because one
        // box is 33 cm in front of another is a trap. Once revealed, DoorInteraction takes them
        // over on its first Update.
        List<Collider> solids = new List<Collider>();
        solids.AddRange(frame.GetComponentsInChildren<Collider>());
        solids.AddRange(hinge.GetComponentsInChildren<Collider>());
        concealed.solids = solids.ToArray();

        BuildVault(root);
    }

    /// <summary>
    /// What is behind it. A dead man's cache: fuel, which is the only thing in this level that
    /// is really time, and the spots the loot table may put its dearest piece on.
    ///
    /// **Nothing here is guaranteed to be worth money.** The cache is worth the walk on its own
    /// and the rest is the same draw as anywhere else, only weighted deeper -- a vault that
    /// always held the television would turn the whole discovery into a route to run every
    /// night, which is exactly what this level was rebuilt to stop being.
    /// </summary>
    static void BuildVault(Transform root)
    {
        Transform vault = Group(root, "Vault");
        Vector3 at = Mountain(120f, -8f);
        vault.localPosition = new Vector3(at.x, 0f, at.z);
        vault.localEulerAngles = new Vector3(0f, 135f, 0f);

        float g = GroundTop;

        Box(vault, "Vault_Crate_A", -2.2f, -1.1f, g, g + 0.8f, 0.6f, 1.7f, plank);
        Box(vault, "Vault_Crate_B", -2.0f, -1.2f, g + 0.8f, g + 1.4f, 0.8f, 1.6f, plank);
        Box(vault, "Vault_Shelf", 1.2f, 2.6f, g + 0.9f, g + 1.0f, -1.4f, -0.5f, plank);

        for (int i = 0; i < 7; i++)
        {
            float r = Random.Range(0.3f, 0.9f);
            Prim(vault, "Vault_Rubble_" + i, PrimitiveType.Sphere,
                 new Vector3(Random.Range(-2.5f, 2.5f), g + r * 0.2f, Random.Range(-2.5f, 2.5f)),
                 new Vector3(r, r * 0.7f, r), rockMat,
                 new Vector3(0f, Random.Range(0f, 360f), 0f), false);
        }

        // The cache. Instantiated from the store's own can, so there is one fuel can in the
        // game and this is not a second copy of it.
        GameObject canPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Store/FuelCan.prefab");
        if (canPrefab == null)
        {
            Debug.LogWarning("No FuelCan prefab to stock the mountain cache with; the vault is empty.");
            return;
        }

        Transform cache = Group(vault, "Cache");

        // Carryables never carve the NavMesh, the same rule every other one in the level follows.
        cache.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        for (int i = 0; i < 2; i++)
        {
            GameObject can = (GameObject)PrefabUtility.InstantiatePrefab(canPrefab, cache);
            can.name = "FuelCan_Cache_" + i;
            can.transform.localPosition = new Vector3(-0.2f + i * 0.75f, g + 0.28f, -1.4f);
            can.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
    }

    /// <summary>
    /// Where loot may be put inside the mountain, as discs on the cave's own centreline.
    ///
    /// <see cref="LootSpawnPointBuilder"/> samples these exactly as it samples the rings out in
    /// the woods, and every candidate goes through the same probe, so a spot in here is a spot
    /// by the same definition as a spot on the kitchen floor. The two numbers that make the
    /// mountain worth the walk are the caps -- thin, and thinner the deeper you go -- and the
    /// extra depth, which only decides WHICH of the run's pieces lands here and never whether
    /// there is one at all.
    /// </summary>
    public static void CaveLootRegions(List<CaveRegion> into)
    {
        into.Add(new CaveRegion("CaveMouth",   Mountain(52f, 21f),    7f,  4f,  2));
        into.Add(new CaveRegion("CavePassage", Mountain(62f, 15f),    8f,  10f, 3));
        into.Add(new CaveRegion("CaveMain",    Mountain(74f, 6f),     10f, 14f, 6));
        into.Add(new CaveRegion("CaveGallery", Mountain(85f, 25f),    8f,  22f, 3));
        into.Add(new CaveRegion("CaveHollow",  Mountain(76f, -18f),   6f,  22f, 3));
        into.Add(new CaveRegion("CaveDeep",    Mountain(97f, -11f),   8f,  30f, 3));
        into.Add(new CaveRegion("CaveFar",     Mountain(110f, -16f),  9f,  34f, 3));
        into.Add(new CaveRegion("CaveVault",   Mountain(120f, -8f),   6f,  48f, 5));
    }

    /// <summary>A place inside the mountain loot may be sampled from, and how deep it counts as.</summary>
    public struct CaveRegion
    {
        public readonly string label;
        public readonly Vector3 centre;
        public readonly float radius;
        public readonly float extraDepth;
        public readonly int cap;

        public CaveRegion(string label, Vector3 centre, float radius, float extraDepth, int cap)
        {
            this.label = label;
            this.centre = centre;
            this.radius = radius;
            this.extraDepth = extraDepth;
            this.cap = cap;
        }
    }

    // ----------------------------------------------------------------- props
    static void BuildProps()
    {
        Transform props = Root("Props");
        float g = GroundTop;

        // Firewood stack against the east wall of the house.
        Transform logs = Group(props, "Woodpile");
        logs.localPosition = new Vector3(HouseHalfX + 1.0f, 0f, 2.2f);
        for (int row = 0; row < 4; row++)
            for (int i = 0; i < 5 - row; i++)
                Prim(logs, "Log_" + row + "_" + i, PrimitiveType.Cylinder,
                     new Vector3(0f, g + 0.16f + row * 0.29f, -0.9f + i * 0.3f + row * 0.15f),
                     new Vector3(0.28f, 0.55f, 0.28f), bark, new Vector3(0f, 0f, 90f), row == 3);

        // Chopping block with the axe still in it.
        Prim(props, "ChoppingBlock", PrimitiveType.Cylinder, new Vector3(9.8f, g + 0.3f, 1.0f), new Vector3(0.6f, 0.3f, 0.6f), bark);
        Prim(props, "Axe_Handle", PrimitiveType.Cylinder, new Vector3(9.8f, g + 0.95f, 1.0f), new Vector3(0.05f, 0.35f, 0.05f), wood, new Vector3(22f, 0f, 0f), false);
        Prim(props, "Axe_Head", PrimitiveType.Cube, new Vector3(9.8f, g + 0.66f, 0.86f), new Vector3(0.09f, 0.2f, 0.26f), metalPale, new Vector3(22f, 0f, 0f), false);

        // Collapsed fence along the path: a boundary that has already failed.
        Transform fence = Group(props, "Fence");
        for (int i = 0; i < 14; i++)
        {
            float z = -21.5f + i * 1.35f;
            if (z > -6.5f) break;
            bool fallen = Random.value < 0.28f;
            Prim(fence, "Post_W_" + i, PrimitiveType.Cube, new Vector3(2.1f, g + (fallen ? 0.07f : 0.55f), z),
                 new Vector3(0.12f, fallen ? 0.12f : 1.1f, 0.12f), wood,
                 fallen ? new Vector3(Random.Range(60f, 110f), Random.Range(0f, 60f), 0f) : new Vector3(0f, Random.Range(-6f, 6f), 0f), false);
            Prim(fence, "Post_E_" + i, PrimitiveType.Cube, new Vector3(7.3f, g + (Random.value < 0.28f ? 0.07f : 0.55f), z),
                 new Vector3(0.12f, 1.1f, 0.12f), wood, new Vector3(0f, Random.Range(-6f, 6f), 0f), false);
        }
        Box(fence, "Rail_W", 2.05f, 2.15f, g + 0.75f, g + 0.87f, -21.5f, -13.0f, plank, false);
        Box(fence, "Rail_E", 7.25f, 7.35f, g + 0.75f, g + 0.87f, -21.5f, -16.0f, plank, false);

        // A rusted-out car at the treeline: the way in, and it is not going back out.
        Transform car = Group(props, "WreckedCar");
        car.localPosition = new Vector3(-12.5f, 0f, -14.0f);
        car.localEulerAngles = new Vector3(0f, 68f, 0f);
        Box(car, "Car_Body", -2.0f, 2.0f, g + 0.35f, g + 1.05f, -0.85f, 0.85f, metalDark);
        Box(car, "Car_Cabin", -0.7f, 0.9f, g + 1.05f, g + 1.6f, -0.75f, 0.75f, metalDark);
        Box(car, "Car_Glass", -0.6f, 0.8f, g + 1.1f, g + 1.5f, -0.78f, 0.78f, glass, false);
        for (int i = 0; i < 4; i++)
            Prim(car, "Car_Wheel" + i, PrimitiveType.Cylinder,
                 new Vector3(i % 2 == 0 ? -1.35f : 1.35f, g + 0.33f, i / 2 == 0 ? -0.85f : 0.85f),
                 new Vector3(0.66f, 0.12f, 0.66f), metalDark, new Vector3(90f, 0f, 0f), false);

        // Well beside the house: a landmark you can navigate back to in the dark.
        Transform well = Group(props, "Well");
        well.localPosition = new Vector3(-10.5f, 0f, 3.5f);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            Prim(well, "Well_Stone" + i, PrimitiveType.Cube,
                 new Vector3(Mathf.Cos(a) * 1.0f, g + 0.35f, Mathf.Sin(a) * 1.0f),
                 new Vector3(0.42f, 0.7f, 0.36f), rockMat, new Vector3(0f, -a * Mathf.Rad2Deg, 0f), i % 3 == 0);
        }
        Prim(well, "Well_Void", PrimitiveType.Cylinder, new Vector3(0f, g + 0.05f, 0f), new Vector3(1.6f, 0.02f, 1.6f), lampDead, default(Vector3), false);
        Prim(well, "Well_PostA", PrimitiveType.Cube, new Vector3(-1.0f, g + 1.3f, 0f), new Vector3(0.12f, 1.9f, 0.12f), wood, default(Vector3), false);
        Prim(well, "Well_PostB", PrimitiveType.Cube, new Vector3(1.0f, g + 1.3f, 0f), new Vector3(0.12f, 1.9f, 0.12f), wood, default(Vector3), false);
        Prim(well, "Well_Beam", PrimitiveType.Cube, new Vector3(0f, g + 2.2f, 0f), new Vector3(2.3f, 0.14f, 0.14f), wood, default(Vector3), false);

        // Rotting sheds and crates against the north side.
        Transform shed = Group(props, "Shed");
        shed.localPosition = new Vector3(-4.0f, 0f, 10.5f);
        shed.localEulerAngles = new Vector3(0f, -12f, 0f);
        Box(shed, "Shed_WallN", -1.8f, 1.8f, g, g + 2.1f, 1.3f, 1.45f, plank);
        Box(shed, "Shed_WallW", -1.8f, -1.65f, g, g + 2.1f, -1.45f, 1.45f, plank);
        Box(shed, "Shed_WallE", 1.65f, 1.8f, g, g + 2.1f, -1.45f, 1.45f, plank);
        Prim(shed, "Shed_Roof", PrimitiveType.Cube, new Vector3(0f, g + 2.35f, 0f), new Vector3(4.1f, 0.12f, 3.4f), roofMat, new Vector3(-8f, 0f, 0f));
        Box(shed, "Shed_Crate", -1.2f, -0.5f, g, g + 0.7f, 0.3f, 1.0f, plank);

        // Nina's drawing, pinned inside the back wall. Optional -- nothing gates on it -- but
        // it is the only place in the level that says what the two odd tools are FOR. Said as
        // a child explaining a game she made up, never as a tutorial: the player should work
        // out that she is describing the UV flashlight and the powder, not be told.
        GameObject drawing = Prim(shed, "Shed_Drawing", PrimitiveType.Cube, new Vector3(0.45f, g + 1.35f, 1.27f),
                                  new Vector3(0.42f, 0.3f, 0.01f), paint, new Vector3(0f, 0f, 3f));
        Readable picture = drawing.AddComponent<Readable>();
        picture.prompt = "Look at the drawing";
        picture.text = "A child's crayon drawing, pinned to the wall.\n\n" +
                       "Two pictures, side by side.\n\n" +
                       "On the left, a purple lamp shining down on the grass. Little footprints " +
                       "glow under it, going off the edge of the paper. Underneath:  " +
                       "THE PURPLE LIGHT SHOWS WHERE THINGS WENT\n\n" +
                       "On the right, a stick girl tipping a jar. Where the dust lands there is " +
                       "a door, drawn over in thick crayon like she pressed hard. Underneath:  " +
                       "THE DUST SHOWS WHAT IS STILL THERE\n\n" +
                       "Across the top, in big wobbly letters:  I CAN SEE THEM AND YOU CANT\n\n" +
                       "In the corner:  NINA";

        // --- old lamps: the only light out here the generator does not own ------------
        // Few, and all within sight of the house: the way in, the shed, the wreck. Past
        // them the woods belong to the moon and the flashlight. None cast shadows (a point
        // light's shadow costs six atlas slices) except the headlight, a spot, which costs one.
        Transform lamps = Group(props, "Lamps");

        // A lantern post where the path leaves the trees, so the way home reads from the treeline.
        Box(lamps, "PathLamp_Post", 6.65f, 6.77f, g, g + 2.7f, -19.86f, -19.74f, wood);
        Box(lamps, "PathLamp_Arm", 6.0f, 6.77f, g + 2.58f, g + 2.66f, -19.84f, -19.76f, wood, false);
        OldLamp(lamps, "PathLamp", new Vector3(6.1f, g + 2.3f, -19.8f), 3.2f, 10f, 0.06f, true);

        // Hurricane lamp under the shed eave, left burning by whoever left.
        OldLamp(shed, "ShedLamp", new Vector3(1.0f, g + 1.8f, -1.4f), 2.4f, 7f, 0.12f, true);

        // One headlight still dying on the wreck's battery, staring off into the trees.
        Light beam = OldLamp(car, "Car_Headlight", new Vector3(2.02f, g + 0.72f, 0.55f), 6f, 14f, 0.3f, false);
        beam.type = LightType.Spot;
        beam.spotAngle = 55f;
        beam.shadows = LightShadows.Soft;
        beam.transform.localEulerAngles = new Vector3(8f, 90f, 0f);   // along the car's +x, dipped

        Light OldLamp(Transform parent, string name, Vector3 pos, float intensity, float range,
                      float stuttersPerSecond, bool hanging)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.64f, 0.32f);   // oil and old filament: warmer than the house
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;

            // Old supply, so it stutters - the house lamps never do.
            go.AddComponent<LightFlicker>().stuttersPerSecond = stuttersPerSecond;

            Prim(go.transform, name + "_Bulb", PrimitiveType.Sphere, Vector3.zero,
                 new Vector3(0.13f, 0.13f, 0.13f), lampWarm, default(Vector3), false);
            if (hanging)
                Prim(go.transform, name + "_Hook", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f),
                     new Vector3(0.02f, 0.12f, 0.02f), metalDark, default(Vector3), false);
            return l;
        }
    }

    // ------------------------------------------------------------- key maker
    /// <summary>
    /// The device on the kitchen table that turns four fragments into the key. Four sockets
    /// in a row with a lamp over each, so how far along you are is readable from the doorway
    /// without opening anything.
    /// </summary>
    static void BuildKeyMaker(Transform table, float topY)
    {
        if (table == null)
        {
            Debug.LogError("No KitchenTable to put the key maker on; Level 1 cannot be finished.");
            return;
        }

        Transform t = Group(table, "KeyMaker");
        t.localPosition = new Vector3(-0.28f, topY, -0.06f);
        t.localEulerAngles = new Vector3(0f, 8f, 0f);

        // A squat iron press. Its body is the collider the player aims at; it stands proud
        // of the tabletop so the aim ray finds it rather than the table.
        Box(t, "Body", -0.42f, 0.42f, 0f, 0.24f, -0.22f, 0.22f, metalDark);
        Box(t, "Backplate", -0.42f, 0.42f, 0.24f, 0.46f, 0.12f, 0.20f, metalPale);

        Transform[] sockets = new Transform[4];
        Light[] lamps = new Light[4];

        for (int i = 0; i < 4; i++)
        {
            float x = -0.3f + i * 0.2f;

            // The socket the fragment is seated in, and the rim around it.
            Box(t, "Rim_" + i, x - 0.07f, x + 0.07f, 0.24f, 0.27f, -0.09f, 0.09f, metalPale);

            Transform socket = Group(t, "Socket_" + i);
            socket.localPosition = new Vector3(x, 0.28f, 0f);
            sockets[i] = socket;

            // A lamp per socket, lit as that fragment goes in. Tiny range: it is a readout,
            // not lighting, and the house already has its own circuit.
            GameObject bulbGo = new GameObject("Lamp_" + i);
            bulbGo.transform.SetParent(t, false);
            bulbGo.transform.localPosition = new Vector3(x, 0.40f, 0.16f);

            Light lamp = bulbGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(0.55f, 0.85f, 1f);
            lamp.intensity = 1.4f;
            lamp.range = 1.2f;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            lamps[i] = lamp;

            Prim(bulbGo.transform, "Bulb_" + i, PrimitiveType.Sphere, Vector3.zero,
                 new Vector3(0.035f, 0.035f, 0.035f), lampWarm, default(Vector3), false);
        }

        // Where the finished key is laid down, clear of the sockets.
        Transform output = Group(t, "KeyRest");
        output.localPosition = new Vector3(0.62f, 0.02f, -0.02f);

        KeyMaker maker = t.gameObject.AddComponent<KeyMaker>();
        maker.sockets = sockets;
        maker.indicatorLights = lamps;
        maker.output = output;
    }

    // ------------------------------------------------------- the way to level 2
    /// <summary>
    /// The UV trail and what it leads to: a line of footprints running out from the near
    /// woods to a mark at the edge of the map, and the door that mark hides.
    ///
    /// Everything here is laid out along local **+z** and the root sits at the house, because
    /// <see cref="Level2Site"/> swings the whole thing onto a random bearing at run time --
    /// so the direction you must walk changes every run while the shape stays as authored.
    ///
    /// The footprints and the circle are render-only and carry <see cref="UVRevealed"/>: they
    /// do not exist to the eye, to a normal flashlight or to the NavMesh. Only the door gets
    /// colliders, and those are off until the powder is scattered.
    /// </summary>
    static void BuildLevel2Site(Transform props)
    {
        Transform root = Group(props, "Level2Site");

        // Nothing here may touch the NavMesh. At bake time the site is still sitting on its
        // authored bearing and the door's colliders are still enabled (Awake has not run in
        // edit mode), so without this the bake would carve a hole where the door ISN'T at
        // run time -- Level2Site swings the whole thing elsewhere before the level starts.
        // Same rule every carryable follows.
        root.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        // Starts well out in the woods, not at the door: the player has to already be
        // exploring with the flashlight on before there is anything to find.
        const float first = 40f;
        const float last = 84f;
        const float step = 2.6f;
        const float circleAt = 88f;
        const float doorAt = 90.5f;

        int n = 0;
        for (float z = first; z <= last; z += step, n++)
        {
            // Left, right, left: a walking line rather than a dotted one.
            float side = (n % 2 == 0 ? -1f : 1f) * 0.22f;

            Transform foot = Group(root, "Step_" + n);
            foot.localPosition = new Vector3(side, 0.02f, z);
            foot.localEulerAngles = new Vector3(0f, Random.Range(-9f, 9f), 0f);

            // A sole and a heel, so it reads as a footprint and not a smudge.
            Prim(foot, "Sole", PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f),
                 new Vector3(0.11f, 0.012f, 0.20f), paint, default(Vector3), false);
            Prim(foot, "Heel", PrimitiveType.Cube, new Vector3(0f, 0f, -0.10f),
                 new Vector3(0.09f, 0.012f, 0.08f), paint, default(Vector3), false);

            foot.gameObject.AddComponent<UVRevealed>();
        }

        // --- the mark at the end of the trail -------------------------------
        Transform circle = Group(root, "UVCircle");
        circle.localPosition = new Vector3(0f, 0.02f, circleAt);

        Transform ring = Group(circle, "Ring");
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Prim(ring, "Arc_" + i, PrimitiveType.Cube,
                 new Vector3(Mathf.Cos(a) * 1.5f, 0f, Mathf.Sin(a) * 1.5f),
                 new Vector3(0.10f, 0.012f, 0.26f), paint,
                 new Vector3(0f, -a * Mathf.Rad2Deg, 0f), false);
        }

        // A cross through the middle, so the circle has a centre to stand on.
        Prim(ring, "Bar_A", PrimitiveType.Cube, Vector3.zero, new Vector3(2.0f, 0.012f, 0.09f), paint, default(Vector3), false);
        Prim(ring, "Bar_B", PrimitiveType.Cube, Vector3.zero, new Vector3(0.09f, 0.012f, 2.0f), paint, default(Vector3), false);

        circle.gameObject.AddComponent<UVRevealed>();

        // The powder left behind afterwards, so the place still reads without the flashlight.
        Transform dust = Group(circle, "Dusting");
        Renderer[] dusting = new Renderer[1];
        dusting[0] = Prim(dust, "Dust", PrimitiveType.Cylinder, Vector3.zero,
                          new Vector3(3.2f, 0.008f, 3.2f), paint, default(Vector3), false).GetComponent<Renderer>();

        // The circle needs a collider so a pour of powder can FIND it -- it is never aimed at
        // or prompted, because a circle that announced itself would give the door away to
        // anyone who walked past without a UV flashlight.
        //
        // A TRIGGER, not a solid: a solid box here would be an invisible wall in the middle of
        // the woods, and the powder's overlap search passes QueryTriggerInteraction.Collide.
        GameObject aim = new GameObject("Circle_Volume");
        aim.transform.SetParent(circle, false);
        aim.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        BoxCollider aimBox = aim.AddComponent<BoxCollider>();
        aimBox.size = new Vector3(3.0f, 1.2f, 3.0f);
        aimBox.isTrigger = true;

        RevealCircle reveal = circle.gameObject.AddComponent<RevealCircle>();
        reveal.dusting = dusting;

        // --- the door itself ------------------------------------------------
        Transform doorRoot = Group(root, "Level2Door");
        doorRoot.localPosition = new Vector3(0f, 0f, doorAt);

        // A stone frame standing in the trees, with nothing behind it.
        Box(doorRoot, "Jamb_W", -1.35f, -0.95f, 0f, 2.75f, -0.25f, 0.25f, rockMat);
        Box(doorRoot, "Jamb_E", 0.95f, 1.35f, 0f, 2.75f, -0.25f, 0.25f, rockMat);
        Box(doorRoot, "Lintel", -1.35f, 1.35f, 2.75f, 3.15f, -0.25f, 0.25f, rockMat);

        Transform hinge = Group(doorRoot, "Door_Hinge");
        hinge.localPosition = new Vector3(-0.95f, 0f, 0f);
        Box(hinge, "Door_Leaf", 0.02f, 1.92f, 0.04f, 2.72f, -0.07f, 0.07f, wood);
        Box(hinge, "Door_Lock", 1.60f, 1.86f, 1.15f, 1.45f, -0.10f, 0.10f, metalPale);

        Level2Door door = doorRoot.gameObject.AddComponent<Level2Door>();
        door.hinge = hinge;
        reveal.door = door;

        Level2Site site = root.gameObject.AddComponent<Level2Site>();
        site.door = door;
    }

    /// <summary>
    /// They pace the edge of the light every night, so the grass there is worn to dirt: the
    /// safe-zone rule written on the ground before a monster is ever seen. Broken rather than
    /// drawn, and render-only, so it costs no collision and no bake.
    /// </summary>
    static void TrampledRing(Transform parent, Vector3 centre, float radius)
    {
        Transform ring = Group(parent, "TrampledRing");
        const int segments = 56;
        float step = Mathf.PI * 2f / segments;

        for (int i = 0; i < segments; i++)
        {
            if (Random.value < 0.3f) continue;

            float a = i * step + Random.Range(-0.02f, 0.02f);
            float r = radius + Random.Range(-0.35f, 0.35f);
            Vector3 p = new Vector3(centre.x + Mathf.Cos(a) * r, GroundTop + 0.007f, centre.z + Mathf.Sin(a) * r);

            // Long axis along the circle's tangent, (-sin a, cos a).
            float yaw = Mathf.Atan2(-Mathf.Sin(a), Mathf.Cos(a)) * Mathf.Rad2Deg + Random.Range(-6f, 6f);
            Prim(ring, "Trample_" + i, PrimitiveType.Cube, p,
                 new Vector3(Random.Range(0.5f, 0.9f), 0.014f, step * radius * Random.Range(0.6f, 0.95f)),
                 dirt, new Vector3(0f, yaw, 0f), false);
        }
    }

    // ------------------------------------------------- gameplay objects + mood
    static void PlaceGameplayObjects(GameObject player, GameObject systems, GameObject generator,
                                     GameObject sunGo, List<Light> houseLights)
    {
        // --- player: on the path, facing the porch ---------------------------
        Undo.RecordObject(player.transform, "Move Player");
        player.transform.position = PlayerSpawn;
        player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);   // looking +z, straight at the house

        GameObject respawn = Find("PlayerRespawn");
        if (respawn != null)
        {
            Undo.RecordObject(respawn.transform, "Move PlayerRespawn");
            respawn.transform.position = PlayerSpawn;
        }

        // --- generator: beside the porch, whole house inside its radius --------
        Undo.RecordObject(generator.transform, "Move Generator");
        generator.transform.position = GeneratorPos;
        generator.transform.rotation = Quaternion.Euler(0f, -24f, 0f);

        Generator gen = generator.GetComponent<Generator>();
        if (gen != null)
        {
            Undo.RecordObject(gen, "Retune Generator");

            // Furthest house corner is ~20.3 m from here, so 22 still wraps the
            // whole building with a little margin - do not shrink it without
            // moving the generator too.
            gen.protectionRadius = 22f;

            // Keep whatever the generator already drove, and add the house lamps
            // so the protection is visible from inside as well as out.
            List<Light> lights = new List<Light>();
            if (gen.poweredLights != null)
                foreach (Light l in gen.poweredLights)
                    if (l != null) lights.Add(l);
            foreach (Light l in houseLights)
                if (l != null && !lights.Contains(l)) lights.Add(l);
            gen.poweredLights = lights.ToArray();

            // Match the state the lamps are authored in.
            foreach (Light l in lights) l.enabled = gen.isRunning;

            // Warm the generator's own lamps: this is the only friendly light here.
            foreach (Transform child in generator.transform)
            {
                Light l = child.GetComponent<Light>();
                if (l == null) continue;
                Undo.RecordObject(l, "Retune generator light");
                l.color = new Color(1f, 0.68f, 0.34f);
                l.intensity = Mathf.Max(l.intensity, 4f);
                l.range = Mathf.Max(l.range, 14f);
                l.shadows = LightShadows.Soft;
            }
        }

        // --- fuel cans: reachable, but at least one is a real walk -------------
        GameObject cans = Find("FuelCans");
        if (cans != null)
        {
            Vector3[] spots =
            {
                new Vector3(10.3f, GroundTop + 0.28f, -7.9f),   // by the generator
                new Vector3(5.6f, FloorTop + 0.28f, 3.6f),      // kitchen
                new Vector3(-4.2f, GroundTop + 0.28f, 10.2f),   // the shed
                new Vector3(-13.4f, GroundTop + 0.28f, -14.8f)  // out at the wreck
            };
            int i = 0;
            foreach (Transform can in cans.transform)
            {
                Undo.RecordObject(can, "Move fuel can");
                can.position = spots[i % spots.Length];
                can.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                i++;
            }
        }

        // --- clock and spawner --------------------------------------------------
        TimeOfDay clock = systems.GetComponent<TimeOfDay>();
        if (clock != null)
        {
            Undo.RecordObject(clock, "Retune TimeOfDay");

            // Enough moon to walk by and to throw canopy shadows, still unmistakably night.
            // The numbers look high because every albedo here is near-black (0.035-0.1).
            clock.nightIntensity = 0.55f;
            clock.nightColor = new Color(0.55f, 0.65f, 0.95f);
            clock.moonYaw = 30f;                 // shines from the south-west onto the porch side
            clock.moonElevation = 40f;
            clock.nightAmbient = 1.8f;
            clock.nightAmbientColor = new Color(0.30f, 0.38f, 0.60f);
            if (clock.sun == null && sunGo != null) clock.sun = sunGo.GetComponent<Light>();

            // Light the Scene view as the game will, without entering Play mode.
            clock.ApplyLighting();
        }

        // --- the forest darkens with distance from the generator ---------------
        Camera eye = player.GetComponentInChildren<Camera>();
        if (eye != null)
        {
            NightDepth depth = eye.GetComponent<NightDepth>();
            if (depth == null) depth = Undo.AddComponent<NightDepth>(eye.gameObject);
            Undo.RecordObject(depth, "Wire NightDepth");
            depth.centre = generator.transform;

            // The gradient has to stretch with the map or the whole world past 45 m reads
            // as one flat black. Clear out to the near woods, fully deep by the mid band.
            depth.clearRadius = 22f;
            depth.deepRadius = 85f;
        }
        else
        {
            Debug.LogError("Player has no camera; NightDepth was not added.");
        }

        // --- a standby lamp on the generator, lit whether it runs or not ------------
        // Its work lights die with the fuel, which is exactly when you have to find it.
        // Under Props, so a rebuild replaces it instead of stacking a second one.
        GameObject props = Find("Props");
        if (props != null)
        {
            GameObject standby = new GameObject("Gen_StandbyLamp");
            standby.transform.SetParent(props.transform, false);
            standby.transform.position = generator.transform.TransformPoint(new Vector3(-0.4f, 0.72f, -0.46f));

            Light l = standby.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.18f);
            l.intensity = 0.6f;
            l.range = 3.5f;
            l.shadows = LightShadows.None;

            Prim(standby.transform, "Gen_StandbyLamp_Bulb", PrimitiveType.Sphere, Vector3.zero,
                 new Vector3(0.06f, 0.06f, 0.06f), lampWarm, default(Vector3), false);

            // Read from the generator's own radius, never hardcoded, so it cannot drift.
            if (gen != null) TrampledRing(props.transform, generator.transform.position, gen.protectionRadius);

            // The UV trail and the door it leads to. Built last, so Level2Site's ground
            // probe at run time has every trunk and rock in the level to sample against.
            BuildLevel2Site(props.transform);
        }

        // --- the key: where its four pieces may hide, measured from the generator --------
        Expedition expedition = systems.GetComponent<Expedition>();
        if (expedition != null)
        {
            Undo.RecordObject(expedition, "Wire Expedition");

            // Fragments are kept out of the safe radius the same way loot depth is measured:
            // from the generator, never from the house's transform.
            expedition.depthCentre = generator.transform;
        }
        else
        {
            Debug.LogWarning("No Expedition on Systems: no key fragments will appear, so Level 1 cannot be finished.");
        }

        // --- the key maker: how many fragments it wants, and what it makes --------
        KeyMaker maker = Object.FindAnyObjectByType<KeyMaker>();
        if (maker != null)
        {
            Undo.RecordObject(maker, "Wire KeyMaker");

            // Read from the Expedition rather than authored twice, so changing how many
            // fragments the level hides cannot leave the device asking for the wrong number.
            if (expedition != null) maker.fragmentsNeeded = expedition.fragmentsInLevel;

            maker.keyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ObjectivePrefabDir + "/CompleteKey.prefab");
            if (maker.keyPrefab == null)
            {
                Debug.LogWarning("No CompleteKey prefab at " + ObjectivePrefabDir +
                                 "; the key maker will consume the fragments and produce nothing.");
            }
        }

        // --- the door at the end of the UV trail ----------------------------------
        Level2Door level2 = Object.FindAnyObjectByType<Level2Door>();
        if (level2 != null)
        {
            Undo.RecordObject(level2, "Wire Level2Door");
            level2.expedition = expedition;
        }

        // --- the corner of the map you need a flashlight for ----------------------
        // On Systems, because which corner is dark is one roll shared by everyone, and it is
        // drawn away from whichever corner the UV trail runs into.
        DarkQuarter dark = systems.GetComponent<DarkQuarter>();
        if (dark == null) dark = Undo.AddComponent<DarkQuarter>(systems);
        Undo.RecordObject(dark, "Wire DarkQuarter");
        dark.centre = generator.transform;
        dark.avoid = level2 != null ? level2.transform : null;

        // Named, not rolled. The mountain is built into the south-east corner and cannot move
        // between runs, so the dark cannot either: what used to be a quarter of the woods you
        // needed a bought flashlight for is now the approach to the rock, and the darkness is
        // what makes the mouth of it something you come upon rather than something you see
        // from the treeline. Level2Site is what keeps the UV trail out of it now -- it asks
        // DarkQuarter.DistanceFromDark, which is only safe to ask BECAUSE the corner is named.
        dark.fixedCorner = DarkQuarter.Quarter.SouthEast;

        // The four quarters meet where the ground is centred, and the gizmo is drawn to its edge.
        dark.mapCentre = Vector3.zero;
        dark.mapHalf = GroundHalf;

        // It stops exactly where the generator's protection does, and is at full strength by
        // 35 m. Measured from edit-mode renders, the woods past about 55 m are already
        // near-black, so a darkening that only bit out there would be invisible -- the band
        // that still reads by moonlight is the one worth taking away.
        dark.innerRadius = 22f;
        dark.fullRadius = 35f;

        // --- loot: deep means far from the generator -----------------------------
        LootSpawner loot = systems.GetComponent<LootSpawner>();
        if (loot != null)
        {
            Undo.RecordObject(loot, "Wire LootSpawner depth");
            loot.depthCentre = generator.transform;

            // There is no fixed place of interest to draw the Large piece towards any more,
            // so it goes wherever depth sends it.
            loot.focus = null;

            // Depth has to be measured against the map it is on: at the old 18/45 every
            // landmark past the near woods counted as equally deep, which would have paid
            // the same for a walk to the garage as for a walk to the far house.
            loot.shallowRadius = 22f;
            loot.deepRadius = 90f;

            // The mountain added a region of its own, so the draw is widened to match rather
            // than quietly thinning the woods out to pay for it. Not balanced yet: retune this
            // against a playtest and against the haul, never on its own.
            loot.minItems = 7;
            loot.maxItems = 11;
        }

        MonsterSpawner spawner = systems.GetComponent<MonsterSpawner>();
        if (spawner != null)
        {
            Undo.RecordObject(spawner, "Retune MonsterSpawner");
            spawner.areaCenter = Vector3.zero;          // the house is the centre now
            spawner.areaRadius = 88f;                   // inside the treeline, off the boundary
            spawner.minDistanceFromPlayer = 26f;
        }

        // The moon itself.
        if (sunGo != null)
        {
            Light sun = sunGo.GetComponent<Light>();
            if (sun != null)
            {
                Undo.RecordObject(sun, "Retune moonlight");
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
            }
        }
    }

    static void ApplyAtmosphere()
    {
        // Fog is what makes the forest feel closed in: past ~25 m there is nothing
        // but silhouette, so the treeline never resolves into a wall of cylinders.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.038f;
        RenderSettings.fogColor = new Color(0.022f, 0.028f, 0.038f);

        // A near-black sky, so the horizon does not out-brighten the generator.
        Shader sky = Shader.Find("Skybox/Procedural");
        if (sky != null)
        {
            const string path = MatDir + "/Env_NightSky.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sky);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sky;
            m.SetFloat("_SunSize", 0.02f);
            m.SetFloat("_AtmosphereThickness", 0.45f);
            m.SetColor("_SkyTint", new Color(0.06f, 0.08f, 0.14f));
            m.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
            m.SetFloat("_Exposure", 0.30f);
            EditorUtility.SetDirty(m);
            RenderSettings.skybox = m;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;   // TimeOfDay drives the colours
        RenderSettings.reflectionIntensity = 0.15f;
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
    }
}
