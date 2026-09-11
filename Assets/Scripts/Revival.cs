using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a revive costs, as a list you can edit. Index 0 is the first revive of the run.
/// Past the end of the list the last price keeps growing, so a team that dies a lot
/// never gets a free one.
/// </summary>
[System.Serializable]
public class RevivePricing
{
    [Tooltip("Price of the 1st, 2nd, 3rd... revive in a run. Retune here; nothing else holds a price.")]
    public int[] prices = { 500, 750, 1100, 1500 };

    [Tooltip("Past the end of the list, each revive costs this many times the one before.")]
    public float growthPastList = 1.35f;

    [Tooltip("Prices past the list are rounded to this, so they read like prices.")]
    public int roundTo = 50;

    /// <summary>The price of revive number <paramref name="index"/> (0 = the first).</summary>
    public int PriceFor(int index)
    {
        if (prices == null || prices.Length == 0) return 0;
        if (index < 0) index = 0;
        if (index < prices.Length) return prices[index];

        float price = prices[prices.Length - 1];
        for (int i = prices.Length; i <= index; i++) price *= growthPastList;

        int step = Mathf.Max(1, roundTo);
        return Mathf.RoundToInt(price / step) * step;
    }
}

/// <summary>
/// The single authority on bringing players back. It leaves a <see cref="PlayerBody"/>
/// where someone dies, decides whether a revive is allowed, performs it, and prices the
/// next one. It does not own the counter -- that is run state, on <see cref="RunState"/>
/// -- and it does not own the prompt, which is the body's.
///
/// A revive needs three things and checks them in one place, <see cref="ReviveRefusal"/>:
/// the body lying in the safe house (<see cref="ReviveZone"/>), a rescuer holding
/// <see cref="Adrenaline"/>, and a player for the body to belong to. It never asks how the
/// body got there, so carrying it home and a future teleporter are the same to it.
///
/// Co-op note: everything here is authority-only, like a sale or a purchase. A client asks
/// the server to revive; the server spends the dose, records the revive and stands the
/// player up, and clients see the body despawn and the player reappear.
/// </summary>
[DisallowMultipleComponent]
public class Revival : MonoBehaviour
{
    public static Revival Instance { get; private set; }

    [Header("Price")]
    public RevivePricing pricing = new RevivePricing();

    [Tooltip("Count doses bought but not yet used towards the price. Without this a team " +
             "could buy four at the first-revive price before anyone had died.")]
    public bool priceIncludesUnspentAdrenaline = true;

    [Header("Bodies")]
    [Tooltip("Left where a player dies. Must carry a PlayerBody.")]
    public PlayerBody bodyPrefab;

    [Header("Revive")]
    [Tooltip("Health a revived player comes back with, as a fraction of their maximum. " +
             "The house regenerates the rest, since that is where a revive happens.")]
    [Range(0.05f, 1f)] public float reviveHealthFraction = 0.5f;

    [Tooltip("The body must lie inside a ReviveZone (the safe house). Clear it only for testing.")]
    public bool requireSafeHouse = true;

    [Header("Wiring")]
    [Tooltip("Where revives are counted. Falls back to RunState.Instance if left empty.")]
    public RunState runState;

    /// <summary>The player who was just brought back.</summary>
    public event System.Action<PlayerVitals> OnRevived = delegate { };

    // One body per player: a player cannot die twice without being revived in between,
    // except in the singleplayer stand-in rule, where a second death replaces the first body.
    private readonly List<PlayerBody> bodies = new List<PlayerBody>();

    // Reused by the grounding raycast: nothing on the death path should allocate.
    private readonly RaycastHit[] groundHits = new RaycastHit[16];

    // Authority seam, as with Wallet, Store and RunState.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>What the next Adrenaline costs. The store shows and charges exactly this.</summary>
    public int NextRevivePrice { get { return pricing.PriceFor(PriceIndex); } }

    /// <summary>Which revive the next dose will pay for, counting from 0.</summary>
    public int PriceIndex
    {
        get
        {
            int used = runState != null ? runState.RevivesUsed : 0;
            return used + (priceIncludesUnspentAdrenaline ? Adrenaline.UnspentCount : 0);
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second Revival exists on " + name + "; destroying it. There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        PlayerVitals.AnyDied += LeaveBody;
    }

    void OnDisable()
    {
        PlayerVitals.AnyDied -= LeaveBody;
    }

    void Start()
    {
        if (runState == null) runState = RunState.Instance;
        if (runState == null)
        {
            Debug.LogError("Revival on " + name + " has no RunState; every revive will cost the first price.", this);
        }
        if (bodyPrefab == null)
        {
            Debug.LogError("Revival on " + name + " has no body prefab; a dead player will leave nothing to revive.", this);
        }
    }

    /// <summary>The body a player left behind, or null.</summary>
    public PlayerBody BodyOf(PlayerVitals player)
    {
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            if (bodies[i] == null) { bodies.RemoveAt(i); continue; }
            if (bodies[i].Owner == player) return bodies[i];
        }
        return null;
    }

    // ------------------------------------------------------------- bodies

    /// <summary>
    /// A player died: put their body on the ground where they fell. The dying player's own
    /// colliders are skipped, and so is anything with a CharacterController, so a monster
    /// standing over them cannot leave the body hanging in mid-air.
    /// </summary>
    private void LeaveBody(PlayerVitals player)
    {
        if (!HasAuthority || player == null || bodyPrefab == null) return;

        PlayerBody previous = BodyOf(player);
        if (previous != null)
        {
            bodies.Remove(previous);
            Destroy(previous.gameObject);
        }

        Vector3 where = GroundUnder(player.LastDeathPosition, player.transform.root);
        Quaternion facing = Quaternion.Euler(0f, player.LastDeathYaw, 0f);

        PlayerBody body = Instantiate(bodyPrefab, where, facing);
        body.name = "Body_" + player.name;
        body.Bind(player);
        bodies.Add(body);
    }

    private Vector3 GroundUnder(Vector3 from, Transform ignore)
    {
        Vector3 origin = from + Vector3.up * 0.5f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        Vector3 place = from;
        for (int i = 0; i < count; i++)
        {
            Transform root = groundHits[i].transform.root;
            if (root == ignore) continue;
            if (root.GetComponent<CharacterController>() != null) continue;

            if (groundHits[i].distance < nearest)
            {
                nearest = groundHits[i].distance;
                place = groundHits[i].point;
            }
        }
        return place;
    }

    // ------------------------------------------------------------- reviving

    /// <summary>
    /// Why this rescuer cannot revive this body right now, or "" if they can. The one
    /// definition of "allowed", so the prompt and the action can never disagree.
    /// </summary>
    public string ReviveRefusal(PlayerBody body, PlayerInteractor rescuer)
    {
        if (body == null || body.Owner == null) return "no one to bring back";
        if (body.IsHeld) return "put it down first";
        if (requireSafeHouse && !ReviveZone.Contains(body.transform.position)) return "bring it into the house to revive";
        if (rescuer == null || rescuer.GetCarried<Adrenaline>() == null) return "needs adrenaline to revive";

        // A dead rescuer is locked out long before this, but the rule should not depend on that.
        if (rescuer.vitals != null && !rescuer.vitals.IsAlive) return "you are dead";
        return "";
    }

    /// <summary>
    /// Spend the rescuer's Adrenaline on a body and stand its owner up where it lay.
    /// The price was paid at the store; this is where the revive is counted, which is what
    /// makes the next dose dearer.
    /// </summary>
    public bool TryRevive(PlayerBody body, PlayerInteractor rescuer)
    {
        if (!HasAuthority) return false;   // clients ask; the server revives
        if (ReviveRefusal(body, rescuer).Length > 0) return false;

        Adrenaline dose = rescuer.GetCarried<Adrenaline>();
        PlayerVitals patient = body.Owner;
        Vector3 where = body.transform.position;
        float yaw = body.transform.eulerAngles.y;

        // Out of the hands the way a sale takes an item: never dropped, then gone.
        rescuer.ConsumeCarried();
        dose.Spend();

        bodies.Remove(body);
        Destroy(body.gameObject);

        if (runState != null) runState.RecordRevive();

        patient.ReviveAt(where, yaw, reviveHealthFraction);
        OnRevived(patient);
        return true;
    }

    [ContextMenu("Log revive prices")]
    private void LogPrices()
    {
        var line = new System.Text.StringBuilder("Revive prices this run: ");
        for (int i = 0; i < 8; i++) line.Append(i == 0 ? "" : ", ").Append("#").Append(i + 1).Append(" $").Append(pricing.PriceFor(i));
        Debug.Log(line.ToString(), this);
    }
}
