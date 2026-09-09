using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates the pool of places loot may appear, under a LootSpawnPoints root.
///
/// It does not decide what spawns or how often -- that is LootSpawner's job. All it does
/// is offer up candidate positions and keep the ones that survive
/// <see cref="LootSpawner.TryResolveSurface"/>. That method is the runtime's own
/// definition of a valid spot, called here rather than reimplemented, so a point can
/// never be authored that the game would then refuse.
///
/// Because validity is probed against real colliders, points land on floors, tabletops
/// and open ground, and never inside a wall, a tree, the counter or the generator.
///
/// Adding somewhere new to search means adding a region below, or dropping a
/// LootSpawnPoint into the scene by hand -- the spawner takes every one it can find.
/// </summary>
public static class LootSpawnPointBuilder
{
    const string RootName = "LootSpawnPoints";

    // Kept in step with PrototypeEnvironmentBuilder's footprint. The probe rejects walls
    // on its own, so these only have to be roughly right.
    const float HouseHalfX = 7.0f;
    const float HouseHalfZ = 5.5f;

    const float IndoorStep = 1.0f;      // grid pitch inside the house
    const float IndoorJitter = 0.3f;    // so a room does not read as graph paper

    const float YardInner = 9f;         // just outside the house and its porch
    const float YardOuter = 20f;
    const int   YardTries = 220;
    const int   YardCap = 28;

    const float FieldInner = 20f;
    const float FieldOuter = 34f;
    const int   FieldTries = 320;
    const int   FieldCap = 24;

    const float PointSpacing = 2.0f;    // no two markers closer than this

    // Each region gets its own budget rather than sharing one pool. A single global cap
    // is spent by whichever region is sampled first, which left the far field with no
    // points at all -- and the far field is most of the reason to leave the house.
    const int   HouseCap = 40;

    // A surface higher than this is a shelf, not somewhere you stand a television.
    const float LargeItemMaxSurfaceHeight = 1.0f;

    [MenuItem("Lab/Loot/Rebuild Loot Spawn Points")]
    public static void BuildFromMenu()
    {
        Build();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    /// <summary>
    /// Rebuilds the point set. Called by PrototypeEnvironmentBuilder too, so a rebuilt
    /// house never leaves loot points standing in its new walls.
    /// </summary>
    public static void Build()
    {
        LootSpawner spawner = Object.FindAnyObjectByType<LootSpawner>();
        GameObject temporary = null;

        if (spawner == null)
        {
            // No spawner in the scene yet: borrow a throwaway one purely for its probe
            // settings, so the tool still works while the scene is being wired up.
            temporary = new GameObject("~LootProbe");
            temporary.hideFlags = HideFlags.HideAndDontSave;
            spawner = temporary.AddComponent<LootSpawner>();
        }

        // The colliders this probes may have been created moments ago by the environment
        // builder; without this the physics scene has not caught up with their transforms.
        Physics.SyncTransforms();

        GameObject root = Find(RootName);
        if (root != null) Undo.DestroyObjectImmediate(root);

        root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Rebuild loot spawn points");

        // Deterministic like the environment builder, and it hands the sequence back so a
        // caller mid-build is not knocked off its own seed. The run-to-run randomness is
        // the spawner's shuffle, not this.
        Random.State restore = Random.state;
        Random.InitState(20260909);

        List<Vector3> accepted = new List<Vector3>();

        int indoor = AddIndoorGrid(spawner, root.transform, accepted);
        int yard = AddRing(spawner, root.transform, accepted, YardInner, YardOuter, YardTries, YardCap, "Yard");
        int field = AddRing(spawner, root.transform, accepted, FieldInner, FieldOuter, FieldTries, FieldCap, "Field");

        Random.state = restore;

        if (temporary != null) Object.DestroyImmediate(temporary);

        int total = indoor + yard + field;
        if (total == 0)
        {
            Debug.LogError("No valid loot spawn points were found at all -- is the environment built?");
            return;
        }

        Debug.Log(string.Format("Loot spawn points rebuilt: {0} total ({1} house, {2} yard, {3} field).",
                                total, indoor, yard, field));
    }

    // ------------------------------------------------------------------- regions

    /// <summary>
    /// A jittered grid over the whole house footprint. There is no need to describe the
    /// rooms: the probe rejects everything that is a wall, a partition or furniture, so
    /// what survives is exactly the walkable floor and the tops of the furniture.
    /// </summary>
    static int AddIndoorGrid(LootSpawner spawner, Transform parent, List<Vector3> accepted)
    {
        int made = 0;

        for (float x = -HouseHalfX + IndoorStep; x < HouseHalfX && made < HouseCap; x += IndoorStep)
        {
            for (float z = -HouseHalfZ + IndoorStep; z < HouseHalfZ && made < HouseCap; z += IndoorStep)
            {
                Vector3 candidate = new Vector3(
                    x + Random.Range(-IndoorJitter, IndoorJitter), 0f,
                    z + Random.Range(-IndoorJitter, IndoorJitter));

                if (TryAdd(spawner, parent, accepted, candidate, "House", made + 1)) made++;
            }
        }

        return made;
    }

    /// <summary>Scattered points in a ring around the house, sampled until the ring is full.</summary>
    static int AddRing(LootSpawner spawner, Transform parent, List<Vector3> accepted,
                       float inner, float outer, int tries, int cap, string label)
    {
        int made = 0;

        for (int i = 0; i < tries && made < cap; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Random.Range(inner * inner, outer * outer));   // even by area
            Vector3 candidate = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

            if (TryAdd(spawner, parent, accepted, candidate, label, made + 1)) made++;
        }

        return made;
    }

    // --------------------------------------------------------------------- point

    static bool TryAdd(LootSpawner spawner, Transform parent, List<Vector3> accepted,
                       Vector3 candidate, string label, int index)
    {
        Vector3 surface;
        if (!spawner.TryResolveSurface(candidate, out surface)) return false;
        if (IsCrowded(accepted, surface)) return false;

        GameObject go = new GameObject(string.Format("LootPoint_{0}_{1:00}", label, index));
        go.transform.SetParent(parent, false);
        go.transform.position = surface;

        LootSpawnPoint point = go.AddComponent<LootSpawnPoint>();

        // A television belongs on the floor or a table, not balanced on a high shelf.
        point.allowLargeItems = surface.y <= LargeItemMaxSurfaceHeight;

        accepted.Add(surface);
        return true;
    }

    static bool IsCrowded(List<Vector3> accepted, Vector3 p)
    {
        float sqr = PointSpacing * PointSpacing;
        for (int i = 0; i < accepted.Count; i++)
        {
            if ((accepted[i] - p).sqrMagnitude < sqr) return true;
        }
        return false;
    }

    static GameObject Find(string name)
    {
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }
}
