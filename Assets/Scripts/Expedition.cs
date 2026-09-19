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

    [Tooltip("How many of the pieces are hidden inside the cave instead of out in the woods. " +
             "One means the cave is on the critical path: the level cannot be finished without " +
             "going in there, with the light that needs.")]
    public int fragmentsInCave = 1;

    [Tooltip("Tries at finding a spot inside the cave before falling back to the woods, so a " +
             "level built without a mountain still puts all its pieces out.")]
    public int caveAttempts = 300;

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

        int inCave = 0;

        for (int i = 0; i < fragmentsInLevel; i++)
        {
            // The first few go in the cave. Deliberately first rather than last: the cave is a
            // much smaller space than the ring, so it should get its pick before the woods have
            // filled the separation list.
            bool wantCave = i < fragmentsInCave;

            // Assigned up front: the compiler cannot see through the short-circuit below that
            // one of the two searches always writes it.
            Vector3 spot = Vector3.zero;
            bool placed = wantCave && FindCaveSpot(out spot);
            if (placed) inCave++;

            if (!placed && !FindSpot(centre, out spot))
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
        line.Append(inCave > 0 ? "  (" + inCave + " in the cave)" : "  (none in the cave)");
        Debug.Log(line.ToString(), this);
    }

    /// <summary>
    /// One spot on the cave floor, or false if there is no cave in this level.
    ///
    /// It samples <see cref="MountainInterior"/>'s volumes rather than the carve skeleton,
    /// because those boxes are already the single description of where the cave is and are
    /// rebuilt with the rock -- so this can never come to disagree with the shape of the
    /// passages. A volume is picked in proportion to its floor area, or the little chambers
    /// would see as many pieces as the long galleries.
    ///
    /// The floor under there is the ground cube, not the mountain: the gravel is render-only.
    /// So the ordinary surface test still applies, and only the "never inside the cave" rule
    /// is inverted.
    /// </summary>
    private bool FindCaveSpot(out Vector3 spot)
    {
        spot = Vector3.zero;

        MountainInterior cave = MountainInterior.Instance;
        if (cave == null || cave.volumes == null || cave.volumes.Length == 0) return false;

        // Total floor area, so the draw is even across the cave rather than even across boxes.
        float total = 0f;
        for (int i = 0; i < cave.volumes.Length; i++)
        {
            if (cave.volumes[i] == null) continue;
            Vector3 s = cave.volumes[i].lossyScale;
            total += Mathf.Abs(s.x) * Mathf.Abs(s.z);
        }
        if (total <= 0f) return false;

        for (int attempt = 0; attempt < Mathf.Max(1, caveAttempts); attempt++)
        {
            Transform volume = PickVolume(cave, total);
            if (volume == null) continue;

            // Well inside the box, not out at its lip. The volumes deliberately overrun each
            // passage by MountainInterior.softness at both ends so consecutive ones overlap,
            // which means their outer edges are buried in solid rock -- sampling the full
            // footprint puts pieces inside the mountain. Measured: a spot at the lip had no
            // ceiling above it at all and no room for a player capsule.
            Vector3 local = new Vector3(Random.Range(-0.32f, 0.32f), 0.45f, Random.Range(-0.32f, 0.32f));
            Vector3 candidate = volume.TransformPoint(local);

            if (TooCloseToAnother(candidate)) continue;

            Vector3 surface;
            if (!Ground(candidate, true, out surface)) continue;
            if (!StandableInCave(cave, surface)) continue;
            if (TooCloseToAnother(surface)) continue;

            spot = surface + Vector3.up * dropClearance;
            return true;
        }

        Debug.LogWarning("Expedition wanted a fragment in the cave and could not place one; " +
                         "it goes in the woods instead.", this);
        return false;
    }

    /// <summary>
    /// Is this a place inside the cave a player could actually walk to and pick something up?
    ///
    /// "Inside a volume" is not enough on its own, and checking only that is what buried the
    /// first attempt in rock. Three things have to be true, and each one caught a real failure:
    ///
    ///   * **Well inside, not in the fade.** The volumes overrun their passages, so the edge of
    ///     one is solid stone.
    ///   * **Rock overhead.** A spot with open sky above it is not in the cave whatever the
    ///     volume says -- that is how the first one came out with 99 m of headroom.
    ///   * **Room to stand.** A player-sized capsule has to fit, or the piece is visible down a
    ///     crack and unreachable, which is worse than not being there.
    /// </summary>
    private bool StandableInCave(MountainInterior cave, Vector3 surface)
    {
        if (cave.Weight(surface + Vector3.up * 1.2f) < 0.95f) return false;

        RaycastHit up;
        if (!Physics.Raycast(surface + Vector3.up * 0.1f, Vector3.up, out up, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;                       // open sky: not under the mountain at all
        }
        if (up.distance < 1.9f) return false;   // too low to stand under

        return !Physics.CheckCapsule(surface + Vector3.up * 0.45f, surface + Vector3.up * 1.55f,
                                     0.38f, ~0, QueryTriggerInteraction.Ignore);
    }

    private Transform PickVolume(MountainInterior cave, float totalArea)
    {
        float pick = Random.Range(0f, totalArea);
        for (int i = 0; i < cave.volumes.Length; i++)
        {
            if (cave.volumes[i] == null) continue;
            Vector3 s = cave.volumes[i].lossyScale;
            pick -= Mathf.Abs(s.x) * Mathf.Abs(s.z);
            if (pick <= 0f) return cave.volumes[i];
        }
        return null;
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
            if (!Ground(candidate, false, out surface)) continue;
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
    private bool Ground(Vector3 candidate, bool wantCave, out Vector3 surface)
    {
        surface = candidate;

        // Where the probe starts matters, and it is not the same question in the two places.
        //
        // Out in the woods the ray drops from high above, so it finds the canopy-free floor.
        // Inside the cave that is exactly wrong: probeHeight above a passage is *above the
        // mountain*, and the ray lands on its roof under 11-37 m of rock. Measured before this
        // was fixed: 1990 of 2000 cave samples hit the roof. So in the cave the ray starts at
        // the sample point itself, which is already inside the passage.
        float start = wantCave ? 0f : probeHeight;
        float reach = wantCave ? 16f : probeHeight * 2f;

        RaycastHit hit;
        if (!Physics.Raycast(candidate + Vector3.up * start, Vector3.down, out hit,
                             reach, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (Vector3.Angle(hit.normal, Vector3.up) > maxSurfaceSlope) return false;

        // Never *on* the mountain, wherever we are aiming. The top of the rock is flat and
        // passes every test above while being somewhere no player can ever stand, so a piece
        // up there would simply never be found. Inside the cave the floor is the ground cube
        // -- the gravel is render-only -- so this rejects ledges without rejecting the cave.
        if (hit.transform.root.name == mountainRootName) return false;

        // Inside the cave, or out of it. One piece is now deliberately hidden in there, which
        // puts the cave on the critical path: it used to be refused outright precisely so the
        // level's optional place stayed optional, and that is the trade being made. Everything
        // else still keeps out, so the other three are always findable without going under.
        MountainInterior cave = MountainInterior.Instance;
        bool underRock = cave != null && cave.Contains(hit.point + Vector3.up * 1.2f);
        if (underRock != wantCave) return false;

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
