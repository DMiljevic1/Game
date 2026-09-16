using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level 1's objective, and deliberately nothing more than that.
///
/// The gate out is chained and the key is in four pieces, scattered somewhere in the woods.
/// Find them, fit them into the lock, leave. There are no stages, no markers, no timer and
/// no quest state beyond one number -- how many pieces are in the gate -- and that number
/// lives on the <see cref="Gate"/>, because it is a property of a physical lock.
///
/// **Every run hides them somewhere new.** There are no authored hiding places at all: each
/// fragment is dropped on a valid patch of forest floor chosen at random, outside the
/// generator's radius and away from the other three. So the player cannot learn where to
/// look, only how to search -- which is the whole point of a level made of woods.
///
/// Co-op note: one authority object owning one piece of shared state, the same shape as
/// Wallet and RunState. Only the authority scatters the fragments and completes the level.
/// </summary>
[DisallowMultipleComponent]
public class Expedition : MonoBehaviour
{
    public static Expedition Instance { get; private set; }

    [Header("The key")]
    [Tooltip("One piece of the gate key. Needs a KeyFragment.")]
    public GameObject fragmentPrefab;

    [Tooltip("How many pieces the key is broken into. The gate needs all of them.")]
    public int fragmentsInLevel = 4;

    [Tooltip("Where they are parented. The Valuables root, so a run of the environment " +
             "builder leaves them alone.")]
    public Transform container;

    [Header("Where they can hide")]
    [Tooltip("Nothing is hidden closer than this to the generator, so the key can never be " +
             "collected from inside the safe radius -- and never inside the house.")]
    public float minDistanceFromBase = 30f;

    [Tooltip("Nothing is hidden further out than this, so no piece ends up jammed against " +
             "the boundary wall.")]
    public float maxDistanceFromBase = 92f;

    [Tooltip("No two pieces land closer together than this: finding one must never mean " +
             "you have nearly found the next.")]
    public float minSeparation = 35f;

    [Tooltip("Where those distances are measured from: the generator. Wired by the builder.")]
    public Transform depthCentre;

    [Header("Placement probe")]
    [Tooltip("How far above the sampled point the ground is searched for.")]
    public float probeHeight = 30f;

    [Tooltip("A surface steeper than this is a rock face, not a forest floor.")]
    [Range(0f, 60f)] public float maxSurfaceSlope = 32f;

    [Tooltip("Clear space needed above the spot, so nothing is hidden inside a trunk or a wall.")]
    public Vector3 fitProbeSize = new Vector3(0.5f, 0.5f, 0.5f);

    [Tooltip("How far above the ground a piece is set down.")]
    public float dropClearance = 0.05f;

    [Tooltip("The root nothing may be hidden on. The mountain: its top is flat enough to pass " +
             "every other test and is somewhere no player can ever stand.")]
    public string mountainRootName = "Mountain";

    /// <summary>Fired once, when the gate opens.</summary>
    public event System.Action OnLevelComplete = delegate { };

    private bool complete;
    private readonly List<KeyFragment> scattered = new List<KeyFragment>();
    private readonly List<Vector3> taken = new List<Vector3>();

    public bool IsComplete { get { return complete; } }

    /// <summary>The pieces put out this run. Read-only; the gate owns how many are fitted.</summary>
    public IReadOnlyList<KeyFragment> Fragments { get { return scattered; } }

    // Authority seam, as with Wallet and RunState.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second Expedition exists on " + name + "; destroying it. There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (HasAuthority) Scatter();
    }

    // ------------------------------------------------------------- scattering

    /// <summary>
    /// Put the four pieces out, once, at the start of the run.
    ///
    /// Positions are drawn fresh every time rather than picked from a list, because a list
    /// is something a player memorises. Three rules shape the draw and each one exists to
    /// keep searching honest:
    ///
    ///   * **Outside <see cref="minDistanceFromBase"/>**, so no piece is ever collectable
    ///     from inside the generator's protection, and none can land in the house.
    ///   * **<see cref="minSeparation"/> apart**, so the four are spread around the compass
    ///     and finding one never means you have nearly found the next.
    ///   * **On real, walkable ground**, checked against actual colliders, so nothing is
    ///     hidden inside a trunk or under the floor where it could never be found.
    /// </summary>
    private void Scatter()
    {
        if (fragmentPrefab == null)
        {
            Debug.LogError("Expedition has no fragment prefab; the gate can never be opened.", this);
            return;
        }

        taken.Clear();
        Vector3 centre = depthCentre != null ? depthCentre.position : Vector3.zero;

        for (int i = 0; i < fragmentsInLevel; i++)
        {
            Vector3 spot;
            if (!FindSpot(centre, out spot))
            {
                Debug.LogError("Expedition could not find anywhere to hide fragment " + i +
                               "; the gate cannot be opened. Loosen minSeparation or the distance band.", this);
                continue;
            }

            GameObject go = Instantiate(fragmentPrefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), container);
            go.name = fragmentPrefab.name + "_" + i;

            KeyFragment fragment = go.GetComponent<KeyFragment>();
            if (fragment == null)
            {
                Debug.LogError("Expedition's fragment prefab has no KeyFragment.", this);
                Destroy(go);
                continue;
            }

            fragment.Describe(i);
            scattered.Add(fragment);
            taken.Add(spot);
        }

        var line = new System.Text.StringBuilder("Expedition scattered ");
        line.Append(scattered.Count).Append(" key fragments:");
        for (int i = 0; i < taken.Count; i++)
        {
            Vector3 d = taken[i] - centre; d.y = 0f;
            line.Append(i == 0 ? " " : ", ").Append(d.magnitude.ToString("0")).Append(" m out");
        }
        Debug.Log(line.ToString(), this);
    }

    /// <summary>
    /// One valid patch of forest floor, or false if the band is too tight to find one.
    /// Tries hard before giving up: the band is a ring nearly 200 m across, so a failure
    /// means the rules are wrong rather than the dice.
    /// </summary>
    private bool FindSpot(Vector3 centre, out Vector3 spot)
    {
        const int attempts = 400;
        for (int i = 0; i < attempts; i++)
        {
            // Even coverage of the ring, rather than crowding the inner edge.
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Random.Range(minDistanceFromBase * minDistanceFromBase,
                                                   maxDistanceFromBase * maxDistanceFromBase));
            Vector3 candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

            if (TooCloseToAnother(candidate)) continue;

            Vector3 surface;
            if (!Ground(candidate, out surface)) continue;
            if (TooCloseToAnother(surface)) continue;

            spot = surface + Vector3.up * dropClearance;
            return true;
        }

        spot = Vector3.zero;
        return false;
    }

    private bool TooCloseToAnother(Vector3 candidate)
    {
        for (int i = 0; i < taken.Count; i++)
        {
            Vector3 d = taken[i] - candidate;
            d.y = 0f;
            if (d.sqrMagnitude < minSeparation * minSeparation) return true;
        }
        return false;
    }

    /// <summary>
    /// The ground under a point, if it is somewhere a fragment could plausibly lie.
    ///
    /// The overlap box is what catches a point inside a trunk or a wall: a downward ray that
    /// *starts* inside a collider passes straight through it and finds the floor beneath, so
    /// the ray alone would happily bury a fragment in a tree. Same reasoning as
    /// LootSpawner.TryResolveSurface.
    /// </summary>
    private bool Ground(Vector3 candidate, out Vector3 surface)
    {
        surface = candidate;

        RaycastHit hit;
        if (!Physics.Raycast(candidate + Vector3.up * probeHeight, Vector3.down, out hit,
                             probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (Vector3.Angle(hit.normal, Vector3.up) > maxSurfaceSlope) return false;

        // Never on the mountain, and never inside it.
        //
        // The top of the rock is flat and passes every test above while being somewhere no
        // player can ever stand, so a piece hidden there would simply never be found. The cave
        // under it is the opposite problem: perfectly reachable, but it is the level's optional
        // place -- dangerous, pitch dark, and behind a bought flashlight. A key fragment in
        // there would quietly make all of that compulsory, which is not what it is for.
        if (hit.transform.root.name == mountainRootName) return false;

        MountainInterior cave = MountainInterior.Instance;
        if (cave != null && cave.Contains(hit.point + Vector3.up * 1.2f)) return false;

        Vector3 box = fitProbeSize * 0.5f;
        if (Physics.CheckBox(hit.point + Vector3.up * (box.y + 0.05f), box,
                             Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        surface = hit.point;
        return true;
    }

    // ------------------------------------------------------------- the ending

    /// <summary>The gate is open. Only <see cref="Gate"/> calls this.</summary>
    public void CompleteLevel()
    {
        if (complete || !HasAuthority) return;

        complete = true;
        OnLevelComplete();
    }
}
