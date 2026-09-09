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

    const float GroundHalf = 55f;     // playable square; the boundary sits on its edges
    const float ForestInner = 14f;
    const float ForestOuter = 44f;

    static readonly Vector3 GeneratorPos = new Vector3(8.8f, 0f, -7.2f);
    static readonly Vector3 PlayerSpawn = new Vector3(4.7f, 1.4f, -12.5f);

    // Objects that carry gameplay wiring. Never destroyed, only repositioned.
    static readonly HashSet<string> Keep = new HashSet<string>
    {
        "Player", "Systems", "Directional Light", "Global Volume",
        "Generator", "PlayerRespawn", "FuelCans", "Flashlight",
        "SellStation", "Store", "Valuables"
    };

    // ------------------------------------------------------------- materials
    const string MatDir = "Assets/Materials/Env";

    static Material grass, dirt, wallExt, wallInt, floorWood, roofMat, wood, plank,
                    fabric, metalDark, metalPale, glass, bark, foliage, foliageAlt,
                    rockMat, bushMat, lampWarm, lampDead, ceramic;

    // Trunk positions, so props and the path can avoid growing inside a tree.
    static readonly List<Vector2> occupied = new List<Vector2>();

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
        bushMat    = Mat("Env_Bush",        new Color(0.040f, 0.058f, 0.038f), 0.03f, 0f, Color.black);
        lampWarm   = Mat("Light_WarmBulb",  new Color(0.320f, 0.230f, 0.130f), 0.30f, 0f, new Color(3.2f, 2.0f, 0.85f));
        lampDead   = Mat("Light_DeadBulb",  new Color(0.180f, 0.180f, 0.170f), 0.60f, 0f, Color.black);
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

        // A shutter hanging off one hinge, and a nailed-up notice: cheap decay cues.
        Prim(detail, "Shutter_Loose", PrimitiveType.Cube, new Vector3(x0 - 0.2f, 1.9f, -2.1f),
             new Vector3(0.06f, 1.1f, 0.7f), plank, new Vector3(0f, 0f, 14f));
        Prim(detail, "Notice", PrimitiveType.Cube, new Vector3(3.35f, 1.85f, z0 - 0.1f),
             new Vector3(0.34f, 0.46f, 0.02f), plank, new Vector3(0f, 0f, -7f), false);

        return house;
    }

    /// <summary>Glass pane in a window opening, optionally boarded over from outside.</summary>
    static void Glass(Transform parent, string name, float centre, float width, float fixedAxis,
                      bool alongX, bool boarded)
    {
        float y0 = FloorTop + 1.0f, y1 = FloorTop + 2.1f;
        float outward = fixedAxis > 0f ? 1f : -1f;

        if (alongX) Box(parent, name + "_Pane", centre - width * 0.5f, centre + width * 0.5f, y0, y1, fixedAxis - 0.02f, fixedAxis + 0.02f, glass, false);
        else Box(parent, name + "_Pane", fixedAxis - 0.02f, fixedAxis + 0.02f, y0, y1, centre - width * 0.5f, centre + width * 0.5f, glass, false);

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

        foreach (Vector2 o in occupied)
            if (Vector2.SqrMagnitude(o - p) < clearance * clearance) return true;
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
        // Main stand. Trees thin out near the house so the clearing reads as one.
        for (int i = 0; i < 200; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner, ForestOuter, 2.9f, out p)) continue;
            occupied.Add(p);
            if (Random.value < 0.72f) Pine(pines, "Pine_" + (n++), p, Random.Range(4.2f, 7.4f));
            else Broadleaf(broadleaf, "Tree_" + (n++), p, Random.Range(3.8f, 5.6f));
        }

        // A closed outer ring: the eye should never find a way out of the woods.
        for (int i = 0; i < 130; i++)
        {
            Vector2 p;
            if (!Scatter(ForestOuter, GroundHalf - 4f, 2.4f, out p)) continue;
            occupied.Add(p);
            Pine(pines, "Pine_Edge_" + (n++), p, Random.Range(5.0f, 8.0f));
        }

        for (int i = 0; i < 22; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner - 2f, ForestOuter, 2.6f, out p)) continue;
            occupied.Add(p);
            DeadTree(dead, "Dead_" + i, p, Random.Range(3.0f, 5.5f));
        }

        for (int i = 0; i < 110; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner - 4f, GroundHalf - 6f, 1.4f, out p)) continue;
            Bush(bushes, "Bush_" + i, p);
        }

        for (int i = 0; i < 60; i++)
        {
            Vector2 p;
            if (!Scatter(ForestInner - 5f, GroundHalf - 6f, 1.6f, out p)) continue;
            Rock(rocks, "Rock_" + i, p);
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
            clock.startPhase = DayPhase.Night;   // the prototype opens in the dark
            clock.nightIntensity = 0.09f;        // a sliver of moon, not a blue day
            clock.nightAmbient = 0.05f;
            clock.nightColor = new Color(0.42f, 0.52f, 0.82f);
            if (clock.sun == null && sunGo != null) clock.sun = sunGo.GetComponent<Light>();

            // Preview night in the Scene view without entering Play mode.
            clock.SetCycleTime(clock.dayLength + clock.duskLength + clock.nightLength * 0.4f);
        }

        MonsterSpawner spawner = systems.GetComponent<MonsterSpawner>();
        if (spawner != null)
        {
            Undo.RecordObject(spawner, "Retune MonsterSpawner");
            spawner.areaCenter = Vector3.zero;          // the house is the centre now
            spawner.areaRadius = 42f;                   // inside the treeline, off the boundary
            spawner.minDistanceFromPlayer = 26f;
        }

        // The moon itself, in case the clock is ever paused mid-day.
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

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;   // TimeOfDay scales this
        RenderSettings.reflectionIntensity = 0.15f;
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
    }
}
