using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fills the level with loot at the start of a run. It owns two decisions and nothing else.
///
/// WHAT you find is drawn from a weighted table, and every weight is derived from the
/// item's own price and bulk -- there is no rarity number to author anywhere. A cheap
/// radio turns up most runs; the television is the once-in-a-while jackpot. That keeps
/// a new valuable "a new prefab and two numbers", exactly as Valuable intends, and it
/// means retuning a price automatically retunes how often you see it.
///
/// WHERE you find it is a shuffle of the LootSpawnPoints in the scene, each re-probed
/// against real geometry, so nothing is ever left inside a wall or under the floor and
/// no two pieces land on top of each other.
///
/// Co-op note: the roll is authority-only, like MonsterSpawner. Clients will receive the
/// spawned items through netcode rather than rolling their own table -- which is exactly
/// why the roll happens once, here, and never per-player.
/// </summary>
[DisallowMultipleComponent]
public class LootSpawner : MonoBehaviour
{
    [Header("What can be found")]
    [Tooltip("One entry per kind of loot. Each prefab needs a Valuable; its price and size " +
             "are the only things this needs to know about it.")]
    public List<GameObject> lootPrefabs = new List<GameObject>();

    [Tooltip("Where spawned loot is parented. The Valuables root, so a run of the " +
             "environment builder leaves it alone.")]
    public Transform container;

    [Header("How much")]
    public int minItems = 6;
    public int maxItems = 9;

    [Header("Rarity")]
    [Tooltip("The price of bread-and-butter loot. Anything dearer than this gets rarer " +
             "along the curve below; anything cheaper gets commoner.")]
    public int commonValue = 40;

    [Tooltip("How sharply price turns into rarity. 1 is straight inverse; higher makes the " +
             "expensive things markedly harder to come by.")]
    [Range(0.5f, 3f)] public float valueExponent = 1.4f;

    [Tooltip("Extra rarity for bulk, on top of price -- heavy loot is rarer than its price " +
             "alone would make it.")]
    [Range(0.05f, 1f)] public float mediumSizeWeight = 0.7f;
    [Range(0.05f, 1f)] public float largeSizeWeight = 0.45f;

    [Tooltip("No item is ever weighted below this, so nothing in the table can become " +
             "impossible to find however dear it gets.")]
    [Range(0.001f, 0.5f)] public float minimumWeight = 0.01f;

    [Header("How many of one kind")]
    public int maxSmallPerRun = 3;
    public int maxMediumPerRun = 2;
    public int maxLargePerRun = 1;

    [Header("Guaranteed large piece")]
    [Tooltip("Every run has at least this many Large pieces, so the deep woods always hold a real " +
             "'this or the case?' decision. WHICH one is still drawn from the table by weight; only " +
             "whether there is one is fixed. It replaces the cheapest piece drawn, so the item count " +
             "is unchanged. 0 = the old behaviour.")]
    public int minLargePerRun = 1;

    [Tooltip("The first Large piece -- guaranteed or rolled -- is put within Focus Radius of this when " +
             "there is room: Tom's camp, so the big prize and the case turn up in the same patch of " +
             "dark. Wired by the builder. Empty = it lands wherever depth sends it.")]
    public Transform focus;
    public float focusRadius = 15f;

    [Header("Depth")]
    [Tooltip("Where depth is measured from: the generator, so deep means far from safety. " +
             "Wired by the builder. Empty = depth plays no part.")]
    public Transform depthCentre;

    [Tooltip("Spots nearer than this all count as shallowest.")]
    public float shallowRadius = 18f;

    [Tooltip("Spots further than this all count as deepest.")]
    public float deepRadius = 45f;

    [Tooltip("How firmly dearer pieces are sent to the deeper spots. 0 = where things land ignores " +
             "price (the old behaviour); 1 = strictly, the dearest always furthest out. Rarity is " +
             "untouched either way: the same items and the same spots are chosen, only the pairing moves.")]
    [Range(0f, 1f)] public float depthBias = 0.75f;

    [Header("Placement")]
    [Tooltip("Nothing spawns this close to another piece of loot, so two items are never " +
             "picked up from the same spot.")]
    public float minItemSeparation = 3f;

    [Tooltip("Height above the point the surface search starts, so a point floating slightly " +
             "over a table still finds the table.")]
    public float probeRise = 1.2f;

    [Tooltip("How far below that to look for a surface.")]
    public float probeDrop = 3f;

    [Tooltip("Steeper than this is a roof or a wall face, not somewhere an object rests.")]
    [Range(0f, 60f)] public float maxSurfaceSlope = 35f;

    [Tooltip("The room an item needs above the surface. Sized for the largest item in the " +
             "game, so anything that fits the probe fits in fact. This is what rejects a " +
             "point buried in a wall -- a downward ray starting inside a wall passes " +
             "straight through it and finds the floor beneath.")]
    public Vector3 fitProbeSize = new Vector3(0.8f, 0.7f, 0.8f);

    [Tooltip("Gap left under an item so it rests on the surface rather than in it.")]
    public float surfaceClearance = 0.02f;

    [Header("Determinism")]
    [Tooltip("Off for a normal run: the layout is different every time and cannot be " +
             "memorised. On only for reproducing a particular arrangement while testing.")]
    public bool useFixedSeed = false;
    public int seed = 0;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Vector3> taken = new List<Vector3>();
    private readonly List<LootSpawnPoint> shuffled = new List<LootSpawnPoint>();
    private readonly List<Draw> draws = new List<Draw>();
    private readonly Collider[] probeHits = new Collider[24];

    // One piece decided but not yet put out: what, where, and whether that spot can take
    // something Large. A pinned piece (the one by the camp) is never re-paired by depth.
    private struct Draw
    {
        public readonly int kind;
        public readonly Vector3 surface;
        public readonly bool allowLarge;
        public readonly bool pinned;

        public Draw(int kind, Vector3 surface, bool allowLarge, bool pinned)
        {
            this.kind = kind;
            this.surface = surface;
            this.allowLarge = allowLarge;
            this.pinned = pinned;
        }
    }

    // Authority seam, as with Generator, Wallet and MonsterSpawner.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>How many pieces of loot are out in the world right now.</summary>
    public int SpawnedCount { get { return spawned.Count; } }

    void Start()
    {
        if (!HasAuthority) return;
        SpawnRun();
    }

    // ------------------------------------------------------------------ spawning

    /// <summary>
    /// Rolls and places a whole run's worth of loot. Public so a future "new expedition"
    /// can re-roll without this having to know what triggered it.
    /// </summary>
    public void SpawnRun()
    {
        if (lootPrefabs == null || lootPrefabs.Count == 0)
        {
            Debug.LogError("LootSpawner has no loot prefabs, so nothing will ever be worth finding.", this);
            return;
        }

        // Only take the random sequence over when a fixed seed was asked for, and give it
        // back afterwards -- the monster spawner draws from the same stream.
        Random.State restore = default(Random.State);
        if (useFixedSeed)
        {
            restore = Random.state;
            Random.InitState(seed);
        }

        ClearSpawned();
        CollectPoints();

        int wanted = Random.Range(minItems, maxItems + 1);

        // WHAT and WHERE, exactly as always: the table decides the items, the shuffle and the
        // probe decide the spots. Nothing is put out until the pairs are settled below.
        for (int i = 0; i < shuffled.Count && draws.Count < wanted; i++)
        {
            LootSpawnPoint point = shuffled[i];
            if (point == null) continue;

            Vector3 surface;
            if (!TryResolveSurface(point.transform.position, out surface)) continue;
            if (IsTooCloseToTakenSpot(surface)) continue;

            int kind = PickKind(point.allowLargeItems, false);
            if (kind < 0) break;                       // every kind has hit its cap

            draws.Add(new Draw(kind, surface, point.allowLargeItems, false));
            taken.Add(surface);
        }

        GuaranteeLarge();
        AssignByDepth();

        for (int i = 0; i < draws.Count; i++) Place(lootPrefabs[draws[i].kind], draws[i].surface);

        if (useFixedSeed) Random.state = restore;

        if (draws.Count < wanted)
        {
            Debug.LogWarning("LootSpawner placed " + draws.Count + " of " + wanted + " items: not enough " +
                             "valid spawn points. Run Lab > Loot > Rebuild Loot Spawn Points.", this);
        }
    }

    /// <summary>
    /// At least <see cref="minLargePerRun"/> Large pieces a run. Which one is still drawn from
    /// the table by weight, so a new Large valuable joins in with no extra authoring. It takes
    /// the place of the cheapest piece drawn, so the number of items is unchanged.
    /// </summary>
    private void GuaranteeLarge()
    {
        int target = Mathf.Min(minLargePerRun, maxLargePerRun);

        for (int have = CountLarge(); have < target; have++)
        {
            int kind = PickKind(true, true);
            if (kind < 0) break;                        // no Large loot in the table at all

            int victim = CheapestReplaceable();
            if (victim < 0)
            {
                Debug.LogWarning("LootSpawner could not fit a guaranteed Large piece this run.", this);
                break;
            }
            draws[victim] = new Draw(kind, draws[victim].surface, true, false);
        }

        // The first Large piece goes beside the camp, however it was drawn -- a lucky natural
        // roll included -- so the prize and the case always compete for the same trip.
        if (focus == null) return;

        int first = FirstLarge();
        if (first < 0) return;

        Vector3 spot;
        if (!TryFocusSpot(out spot))
        {
            Debug.LogWarning("LootSpawner found no room for a Large piece within " + focusRadius +
                             " m of " + focus.name + "; it stays where it was drawn.", this);
            return;
        }

        taken.Add(spot);
        draws[first] = new Draw(draws[first].kind, spot, true, true);
    }

    /// <summary>A spot that can take a Large piece within <see cref="focusRadius"/> of the focus.</summary>
    private bool TryFocusSpot(out Vector3 spot)
    {
        spot = Vector3.zero;
        if (focus == null) return false;

        float sqr = focusRadius * focusRadius;
        for (int i = 0; i < shuffled.Count; i++)
        {
            LootSpawnPoint point = shuffled[i];
            if (point == null || !point.allowLargeItems) continue;

            Vector3 d = point.transform.position - focus.position;
            d.y = 0f;
            if (d.sqrMagnitude > sqr) continue;

            Vector3 surface;
            if (!TryResolveSurface(point.transform.position, out surface)) continue;
            if (IsTooCloseToTakenSpot(surface)) continue;

            spot = surface;
            return true;
        }
        return false;
    }

    /// <summary>The cheapest non-Large piece standing on a spot that could take a Large one.</summary>
    private int CheapestReplaceable()
    {
        int best = -1;
        int bestValue = int.MaxValue;
        for (int i = 0; i < draws.Count; i++)
        {
            if (IsLarge(draws[i].kind)) continue;
            if (!draws[i].allowLarge) continue;

            int value = ValueOf(draws[i].kind);
            if (value < bestValue)
            {
                bestValue = value;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Who stands where: dearer pieces go to the deeper spots. The items and the spots are the
    /// ones the table and the shuffle already chose, so rarity, caps and separation are all
    /// untouched -- only the pairing moves. Chance blurs the order by (1 - depthBias), so it is
    /// a tendency players can learn, never a law they can rely on.
    /// </summary>
    private void AssignByDepth()
    {
        if (depthCentre == null || depthBias <= 0f) return;

        List<int> free = new List<int>();
        for (int i = 0; i < draws.Count; i++)
        {
            if (!draws[i].pinned) free.Add(i);
        }
        if (free.Count < 2) return;

        List<Draw> spots = new List<Draw>(free.Count);
        List<float> keys = new List<float>(free.Count);
        List<int> kinds = new List<int>(free.Count);
        for (int i = 0; i < free.Count; i++)
        {
            Draw d = draws[free[i]];
            spots.Add(d);
            keys.Add(Mathf.Lerp(Random.value, Depth01(d.surface), depthBias));
            kinds.Add(d.kind);
        }

        // Dearest first, and Large before anything: only some spots can take them.
        kinds.Sort(CompareDearestFirst);

        bool[] used = new bool[spots.Count];
        for (int k = 0; k < kinds.Count; k++)
        {
            bool large = IsLarge(kinds[k]);
            int best = -1;
            for (int s = 0; s < spots.Count; s++)
            {
                if (used[s] || (large && !spots[s].allowLarge)) continue;
                if (best < 0 || keys[s] > keys[best]) best = s;
            }

            // Cannot happen -- every Large piece was drawn on a spot that could take it, and they
            // choose first -- but a missed piece is better than two on one spot.
            if (best < 0) continue;

            used[best] = true;
            draws[free[k]] = new Draw(kinds[k], spots[best].surface, spots[best].allowLarge, false);
        }
    }

    private int CompareDearestFirst(int a, int b)
    {
        bool largeA = IsLarge(a);
        bool largeB = IsLarge(b);
        if (largeA != largeB) return largeA ? -1 : 1;
        return ValueOf(b).CompareTo(ValueOf(a));
    }

    /// <summary>0 at <see cref="shallowRadius"/> from the depth centre or nearer, 1 at <see cref="deepRadius"/> or further.</summary>
    private float Depth01(Vector3 p)
    {
        Vector3 d = p - depthCentre.position;
        d.y = 0f;
        return Mathf.InverseLerp(shallowRadius, deepRadius, d.magnitude);
    }

    private bool IsLarge(int kind)
    {
        Valuable v = ValuableOf(lootPrefabs[kind]);
        return v != null && v.size == ValuableSize.Large;
    }

    private int ValueOf(int kind)
    {
        Valuable v = ValuableOf(lootPrefabs[kind]);
        return v != null ? v.value : 0;
    }

    private int CountLarge()
    {
        int n = 0;
        for (int i = 0; i < draws.Count; i++)
        {
            if (IsLarge(draws[i].kind)) n++;
        }
        return n;
    }

    private int FirstLarge()
    {
        for (int i = 0; i < draws.Count; i++)
        {
            if (IsLarge(draws[i].kind)) return i;
        }
        return -1;
    }

    /// <summary>Removes loot this spawner put out. Anything else in the level is untouched.</summary>
    public void ClearSpawned()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) Destroy(spawned[i]);
        }
        spawned.Clear();
        taken.Clear();
        draws.Clear();
    }

    private void Place(GameObject prefab, Vector3 surface)
    {
        // A random facing, so the same radio twice over does not read as a copy.
        GameObject go = Instantiate(prefab, surface, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), container);
        go.name = prefab.name;

        // Sit it ON the surface rather than at it: an item's pivot is not always its base.
        Bounds b;
        if (TryGetBounds(go, out b))
        {
            go.transform.position += Vector3.up * ((surface.y + surfaceClearance) - b.min.y);
        }

        spawned.Add(go);
        taken.Add(go.transform.position);
    }

    // ------------------------------------------------------------------- rarity

    /// <summary>
    /// The one place a price and a size become odds. Dearer is rarer, bulkier is rarer
    /// still, and the floor keeps the dearest thing in the game merely uncommon rather
    /// than mythical.
    /// </summary>
    public float SpawnWeight(Valuable v)
    {
        if (v == null) return 0f;

        float w = Mathf.Pow(Mathf.Max(1, commonValue) / (float)Mathf.Max(1, v.value), valueExponent);

        if (v.size == ValuableSize.Medium) w *= mediumSizeWeight;
        else if (v.size == ValuableSize.Large) w *= largeSizeWeight;

        return Mathf.Max(w, minimumWeight);
    }

    /// <summary>How many of one kind a single run may contain. Derived from size, so a new
    /// item inherits a sensible cap without anyone authoring one.</summary>
    public int MaxPerRun(Valuable v)
    {
        if (v == null) return 0;

        switch (v.size)
        {
            case ValuableSize.Large:  return maxLargePerRun;
            case ValuableSize.Medium: return maxMediumPerRun;
            default:                  return maxSmallPerRun;
        }
    }

    private int PickKind(bool allowLarge, bool largeOnly)
    {
        float total = 0f;
        for (int i = 0; i < lootPrefabs.Count; i++)
        {
            if (IsEligible(i, allowLarge, largeOnly)) total += SpawnWeight(ValuableOf(lootPrefabs[i]));
        }
        if (total <= 0f) return -1;

        float roll = Random.value * total;
        int last = -1;

        for (int i = 0; i < lootPrefabs.Count; i++)
        {
            if (!IsEligible(i, allowLarge, largeOnly)) continue;

            last = i;
            roll -= SpawnWeight(ValuableOf(lootPrefabs[i]));
            if (roll <= 0f) return i;
        }

        return last;                                   // rounding only; the table is not empty
    }

    private bool IsEligible(int index, bool allowLarge, bool largeOnly)
    {
        Valuable v = ValuableOf(lootPrefabs[index]);
        if (v == null) return false;
        if (!allowLarge && v.size == ValuableSize.Large) return false;
        if (largeOnly && v.size != ValuableSize.Large) return false;

        return CountOf(index) < MaxPerRun(v);
    }

    /// <summary>How many of this kind the run has drawn so far.</summary>
    private int CountOf(int kind)
    {
        int n = 0;
        for (int i = 0; i < draws.Count; i++)
        {
            if (draws[i].kind == kind) n++;
        }
        return n;
    }

    private static Valuable ValuableOf(GameObject prefab)
    {
        return prefab == null ? null : prefab.GetComponent<Valuable>();
    }

    // ---------------------------------------------------------------- placement

    private void CollectPoints()
    {
        shuffled.Clear();

        LootSpawnPoint[] all = FindObjectsByType<LootSpawnPoint>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].isActiveAndEnabled) shuffled.Add(all[i]);
        }

        if (shuffled.Count == 0)
        {
            Debug.LogError("LootSpawner found no LootSpawnPoints; nothing can be placed. " +
                           "Run Lab > Loot > Rebuild Loot Spawn Points.", this);
            return;
        }

        // Fisher-Yates: which points get used is the randomisation, so it must be a real
        // shuffle and not a random start index into a fixed order.
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            LootSpawnPoint tmp = shuffled[i];
            shuffled[i] = shuffled[j];
            shuffled[j] = tmp;
        }

        // Loot that is already in the level -- fuel cans, the torch -- counts as a taken
        // spot, so a run never stands a spawned item inside one of them.
        Carryable[] existing = FindObjectsByType<Carryable>();
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null && !existing[i].IsHeld) taken.Add(existing[i].transform.position);
        }

        // Tom's case is put out on the focus by Expedition, possibly after this runs, so its
        // spot is reserved here rather than found.
        if (focus != null) taken.Add(focus.position);
    }

    private bool IsTooCloseToTakenSpot(Vector3 p)
    {
        float sqr = minItemSeparation * minItemSeparation;
        for (int i = 0; i < taken.Count; i++)
        {
            if ((taken[i] - p).sqrMagnitude < sqr) return true;
        }
        return false;
    }

    /// <summary>
    /// Turns a marker's position into the spot an item actually rests on, or refuses it.
    ///
    /// This is the whole definition of a valid spawn point and there is deliberately only
    /// one copy: the editor tool that generates the markers calls this same method, so
    /// what is authored and what is used can never drift apart.
    /// </summary>
    public bool TryResolveSurface(Vector3 point, out Vector3 surface)
    {
        surface = point;

        RaycastHit hit;
        if (!Physics.Raycast(point + Vector3.up * probeRise, Vector3.down, out hit,
                             probeRise + probeDrop, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;                              // nothing underneath at all
        }

        if (IsIgnored(hit.collider)) return false;      // landed on a player, a monster or an item
        if (Vector3.Angle(hit.normal, Vector3.up) > maxSurfaceSlope) return false;   // a wall or a roof

        // Is there actually room to stand something here? The box sits entirely above the
        // surface, so the surface itself can never be what rejects it.
        Vector3 centre = hit.point + Vector3.up * (fitProbeSize.y * 0.5f + surfaceClearance);
        int n = Physics.OverlapBoxNonAlloc(centre, fitProbeSize * 0.5f, probeHits,
                                           Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        if (n >= probeHits.Length) return false;        // too crowded to judge; refuse rather than guess

        for (int i = 0; i < n; i++)
        {
            if (probeHits[i] == hit.collider) continue;
            if (IsIgnored(probeHits[i])) continue;
            return false;                               // a wall, a tree, the counter, a chair
        }

        surface = hit.point;
        return true;
    }

    /// <summary>Things that must not count as scenery: they move, or they are loot themselves.</summary>
    private static bool IsIgnored(Collider c)
    {
        if (c == null) return true;
        if (c.GetComponentInParent<Carryable>() != null) return true;
        if (c.GetComponentInParent<CharacterController>() != null) return true;   // player and monsters
        return false;
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds(go.transform.position, Vector3.zero);

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    // -------------------------------------------------------------------- debug

    /// <summary>Prints what the current tuning actually means, so the curve is never a black box.</summary>
    [ContextMenu("Log spawn odds")]
    public void LogSpawnOdds()
    {
        float total = 0f;
        for (int i = 0; i < lootPrefabs.Count; i++) total += SpawnWeight(ValuableOf(lootPrefabs[i]));
        if (total <= 0f) { Debug.LogWarning("LootSpawner has no weighted loot.", this); return; }

        var sb = new System.Text.StringBuilder("Loot odds per draw (");
        sb.Append(minItems).Append(" to ").Append(maxItems).Append(" drawn per run):\n");

        for (int i = 0; i < lootPrefabs.Count; i++)
        {
            Valuable v = ValuableOf(lootPrefabs[i]);
            if (v == null) continue;

            float w = SpawnWeight(v);
            sb.AppendFormat("  {0,-22} {1,-5} {2,-6} weight {3,6:F3}  {4,5:F1}%  max {5}/run\n",
                            v.itemName, v.value, v.size, w, 100f * w / total, MaxPerRun(v));
        }

        sb.AppendFormat("At least {0} Large piece(s) a run{1}.\n", minLargePerRun,
                        focus != null ? ", put within " + focusRadius + " m of " + focus.name + " when there is room" : "");
        sb.AppendFormat("Depth bias {0:F2}: dearer pieces tend to land further from {1}.\n", depthBias,
                        depthCentre != null ? depthCentre.name : "(nothing -- depth is off)");

        Debug.Log(sb.ToString(), this);
    }
}
