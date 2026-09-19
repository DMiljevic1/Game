using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a revive costs: one flat price, the same every time. Deliberately not a curve --
/// a price that climbs with every death punishes the run that is already going badly, and
/// the question a revive should pose is "is it worth $500 and the walk out there", never
/// "can we still afford one". Retune the single number; nothing else holds a price.
/// </summary>
[System.Serializable]
public class RevivePricing
{
    [Tooltip("What every revive costs. Flat by design -- the same for the first of the run and the tenth.")]
    public int price = 500;

    /// <summary>What a revive costs. The same answer however many have been used.</summary>
    public int Price { get { return Mathf.Max(0, price); } }
}

/// <summary>
/// The single authority on bringing players back. It leaves a <see cref="PlayerBody"/>
/// where someone dies, decides whether a revive is allowed, performs it, and prices the
/// next one. It does not own the counter -- that is run state, on <see cref="RunState"/>
/// -- and it does not own the prompt, which is the body's.
///
/// A revive needs a body on the ground, a player for it to belong to and a rescuer holding
/// <see cref="Adrenaline"/>, all checked in one place, <see cref="ReviveRefusal"/>. It never
/// asks how the body got there, so carrying it home and a future teleporter are the same.
///
/// **A revive is the same act everywhere.** Seconds of standing over someone, a noise far
/// past a sprinting footstep going out the whole time, the dose committed the moment it
/// starts, and nothing to show for it if the rescuer is driven off. There is deliberately
/// no faster or quieter revive at home, and nothing here asks where the body is lying.
///
/// What changes with the place is only whether that noise can reach you: inside a running
/// generator's radius the monsters it pulls cannot get in (see Generator.IsPointProtected
/// and Monster.KeepOutOfSafeZone, which already hold every noise made in the light at the
/// boundary). Carrying a teammate home buys *safety*, never speed or silence -- and the
/// moment the fuel runs out the house is just another place to kneel down in.
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

    [Header("Bodies")]
    [Tooltip("Left where a player dies. Must carry a PlayerBody.")]
    public PlayerBody bodyPrefab;

    [Header("Revive")]
    [Tooltip("Seconds of standing over the body. The same everywhere -- there is no faster " +
             "revive at home; the generator's radius buys safety, not speed.")]
    public float reviveSeconds = 5.5f;

    [Tooltip("How far the revive carries, in metres. Well past a sprinting footstep (20), so " +
             "anything within earshot comes to look. It is emitted inside the house exactly " +
             "as it is outside -- protection is what makes it harmless there, not silence. " +
             "A world noise, so crouching does not muffle it.")]
    public float reviveNoiseRadius = 25f;

    [Tooltip("Seconds between those noises while the revive runs, so it keeps pulling rather " +
             "than being one event a monster can walk past the end of.")]
    public float reviveNoiseInterval = 1.5f;

    [Tooltip("How far the rescuer may stray from the body before the attempt fails. Walking " +
             "away is a real loss: the dose is committed the moment the needle goes in.")]
    public float reviveStayWithin = 3f;

    [Header("Wiring")]
    [Tooltip("Where revives are counted. Falls back to RunState.Instance if left empty.")]
    public RunState runState;

    /// <summary>The player who was just brought back.</summary>
    public event System.Action<PlayerVitals> OnRevived = delegate { };

    // One body per player: a player cannot die twice without being revived in between,
    // except in the singleplayer stand-in rule, where a second death replaces the first body.
    private readonly List<PlayerBody> bodies = new List<PlayerBody>();

    /// <summary>
    /// One injection in progress. The dose is already spent when this exists -- committing
    /// it up front is what makes walking away a loss rather than a free look at the timer.
    /// </summary>
    private class ReviveAttempt
    {
        public PlayerBody body;
        public PlayerInteractor rescuer;
        public float endTime;
        public float nextNoiseTime;
    }

    // One per rescuer, so two players can work on two bodies and neither can start a
    // second attempt on a body somebody else is already reviving.
    private readonly List<ReviveAttempt> attempts = new List<ReviveAttempt>();

    // Reused by the grounding raycast: nothing on the death path should allocate.
    private readonly RaycastHit[] groundHits = new RaycastHit[16];

    // Authority seam, as with Wallet, Store and RunState.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>What an Adrenaline costs. The store shows and charges exactly this, every time.</summary>
    public int NextRevivePrice { get { return pricing.Price; } }

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

        // Everything they were carrying goes onto the body rather than onto the floor, so
        // a teammate can bring the whole lot home in one trip -- or search the body and
        // leave its owner lying there. PlayerVitals fires AnyDied before OnDied precisely
        // so this happens before the interactor's own ground drop.
        body.TakeBelongings(player.GetComponent<PlayerInteractor>());
    }

    /// <summary>
    /// The ground under a point, skipping <paramref name="ignore"/> and anything walking
    /// around. Public so a body tipping its belongings out uses the same footing rule the
    /// body itself was placed with, rather than a third copy of this raycast.
    /// </summary>
    public Vector3 GroundUnder(Vector3 from, Transform ignore)
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
        if (AttemptOn(body) != null) return "already being revived";
        if (rescuer == null || rescuer.GetCarried<Adrenaline>() == null) return "needs adrenaline to revive";

        // A dead rescuer is locked out long before this, but the rule should not depend on that.
        if (rescuer.vitals != null && !rescuer.vitals.IsAlive) return "you are dead";
        return "";
    }

    /// <summary>
    /// Spend the rescuer's Adrenaline on a body and stand its owner up where it lay.
    /// The price was paid at the store; this is where the revive is counted, for the run's
    /// own tally -- the price is flat, so it does not change what the next dose costs.
    /// </summary>
    public bool TryRevive(PlayerBody body, PlayerInteractor rescuer)
    {
        if (!HasAuthority) return false;   // clients ask; the server revives
        if (ReviveRefusal(body, rescuer).Length > 0) return false;

        Adrenaline dose = rescuer.GetCarried<Adrenaline>();

        // Out of the hands the way a sale takes an item: never dropped, then gone. The dose
        // is committed before the timer starts, which is what makes an abandoned revive a
        // real loss rather than a look at a progress bar.
        rescuer.ConsumeCarried();
        dose.Spend();

        // Nothing here asks where the body is. One revive, the same length and the same
        // noise wherever it happens; the generator decides whether that noise can reach you.
        attempts.Add(new ReviveAttempt
        {
            body = body,
            rescuer = rescuer,
            endTime = Time.time + Mathf.Max(0.1f, reviveSeconds),
            nextNoiseTime = Time.time,      // the first cry goes out immediately
        });
        return true;
    }

    // ------------------------------------------------------------- revives in progress

    /// <summary>
    /// Is this spot covered by a running generator? Nothing about the revive changes with
    /// the answer -- it is the same length and the same noise either way. It is here only
    /// so the prompt can say whether the racket is going to cost the player anything.
    /// </summary>
    public static bool IsProtectedSpot(PlayerBody body)
    {
        return body != null && Generator.IsPointProtected(body.transform.position);
    }

    /// <summary>True while someone is stood over this body with the needle in.</summary>
    public bool IsBeingRevived(PlayerBody body)
    {
        return AttemptOn(body) != null;
    }

    /// <summary>Seconds left on the injection, or 0 if there is none.</summary>
    public float ReviveSecondsLeft(PlayerBody body)
    {
        ReviveAttempt attempt = AttemptOn(body);
        return attempt == null ? 0f : Mathf.Max(0f, attempt.endTime - Time.time);
    }

    /// <summary>
    /// Give up on an injection. The dose is not refunded -- it went in the moment the
    /// attempt started -- so this is the player choosing to run rather than a free undo.
    /// </summary>
    public bool CancelRevive(PlayerBody body)
    {
        ReviveAttempt attempt = AttemptOn(body);
        if (attempt == null) return false;

        attempts.Remove(attempt);
        return true;
    }

    private ReviveAttempt AttemptOn(PlayerBody body)
    {
        for (int i = attempts.Count - 1; i >= 0; i--)
        {
            if (attempts[i].body == null) { attempts.RemoveAt(i); continue; }
            if (attempts[i].body == body) return attempts[i];
        }
        return null;
    }

    void Update()
    {
        if (!HasAuthority) return;

        for (int i = attempts.Count - 1; i >= 0; i--)
        {
            ReviveAttempt attempt = attempts[i];

            // Every way an attempt can fall apart, in one place: the body or the rescuer
            // gone, the rescuer killed while kneeling there, or the rescuer walking off.
            if (attempt.body == null || attempt.rescuer == null
                || attempt.body.Owner == null
                || (attempt.rescuer.vitals != null && !attempt.rescuer.vitals.IsAlive)
                || Vector3.Distance(attempt.rescuer.transform.position, attempt.body.transform.position)
                   > reviveStayWithin)
            {
                attempts.RemoveAt(i);
                continue;
            }

            // Straight to the bus rather than through the rescuer's NoiseEmitter: this is
            // the patient and the needle, not the rescuer's own body, so crouching over
            // someone does not make bringing them round any quieter.
            if (Time.time >= attempt.nextNoiseTime)
            {
                attempt.nextNoiseTime = Time.time + Mathf.Max(0.2f, reviveNoiseInterval);
                Noise.Emit(attempt.body.transform.position, reviveNoiseRadius,
                           attempt.rescuer.gameObject);
            }

            if (Time.time >= attempt.endTime)
            {
                attempts.RemoveAt(i);
                CompleteRevive(attempt.body);
            }
        }
    }

    /// <summary>
    /// Stand the body's owner up where it lies. The dose has already been spent by the
    /// time anything gets here, so this is the one tail both routes share.
    /// </summary>
    private void CompleteRevive(PlayerBody body)
    {
        PlayerVitals patient = body.Owner;
        Vector3 where = body.transform.position;
        float yaw = body.transform.eulerAngles.y;

        // Whatever they were still carrying goes back to the world at their feet, as an
        // ordinary pickup -- they come round empty-handed and have to gather their kit up.
        body.Spill();

        bodies.Remove(body);
        Destroy(body.gameObject);

        if (runState != null) runState.RecordRevive();

        patient.ReviveAt(where, yaw);
        OnRevived(patient);
    }

    void OnGUI()
    {
        // A plain observer of the attempt list. Singleplayer today, so there is at most
        // one; in co-op this readout moves to the rescuer's own HUD.
        for (int i = 0; i < attempts.Count; i++)
        {
            if (attempts[i].body == null) continue;

            Hud.RowRight(3, string.Format("Reviving {0}   {1:0.0}s   [LOUD]",
                                          attempts[i].body.itemName, ReviveSecondsLeft(attempts[i].body)),
                         new Color(1f, 0.55f, 0.4f));
            break;
        }
    }
}
