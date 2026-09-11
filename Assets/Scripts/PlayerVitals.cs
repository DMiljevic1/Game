using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player health, and what happens when a monster reaches you.
/// Regenerates only inside generator protection, so the house is where you recover
/// and the field never is.
///
/// Death is a state, not a teleport: a dead player stays dead until something revives
/// them. What that looks like -- the locked controls, the body left behind, the price of
/// bringing them back -- belongs to PlayerDeathLock, Revival and RunState, which all
/// react to the events here. This class knows nothing about any of them.
///
/// Co-op note: health and the alive/dead state are owned by the authority. The static
/// registry is every player in the level, not per-player state.
/// </summary>
[DisallowMultipleComponent]
public class PlayerVitals : MonoBehaviour
{
    // Every player in the level, so the run can ask "is anyone still standing?" without a
    // scene search. Scene-object registry, like Generator's.
    private static readonly List<PlayerVitals> all = new List<PlayerVitals>();

    /// <summary>Every player currently in the level, alive or dead.</summary>
    public static IReadOnlyList<PlayerVitals> All { get { return all; } }

    /// <summary>Any player died. Run-level systems listen here rather than to each player.</summary>
    public static event System.Action<PlayerVitals> AnyDied = delegate { };

    /// <summary>Any player got back up, by revive or by respawn.</summary>
    public static event System.Action<PlayerVitals> AnyRevived = delegate { };

    public float maxHealth = 100f;
    public float health = 100f;

    [Tooltip("Health per second regained while inside a running generator's radius.")]
    public float regenInSafeZone = 3f;
    [Tooltip("Seconds without damage before regeneration starts.")]
    public float regenDelay = 5f;

    [Tooltip("Where Respawn puts the player. Only the singleplayer fallback uses it now; " +
             "a revive stands the player up where their body lies.")]
    public Transform respawnPoint;

    public event System.Action OnDamaged = delegate { };
    public event System.Action OnDied = delegate { };

    /// <summary>Back on their feet, by revive or by respawn.</summary>
    public event System.Action OnRevived = delegate { };

    private float lastDamageTime = -999f;
    private float lastDamageAmount;
    private Vector3 lastDeathPosition;
    private float lastDeathYaw;
    private CharacterController controller;

    public bool IsAlive { get { return health > 0f; } }
    public float HealthNormalized { get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(health / maxHealth); } }

    /// <summary>Size of the most recent hit, so feedback can scale itself without OnDamaged carrying a payload.</summary>
    public float LastDamageAmount { get { return lastDamageAmount; } }

    /// <summary>
    /// Where the player was standing when they were last killed. Pinned before OnDied
    /// fires, so the carried-item drop and the body both land on the same spot.
    /// </summary>
    public Vector3 LastDeathPosition { get { return lastDeathPosition; } }

    /// <summary>Which way the player was facing when they were last killed, so the body lies that way.</summary>
    public float LastDeathYaw { get { return lastDeathYaw; } }

    // Authority seam, as with Wallet. Becomes IsServer once netcode is in.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = maxHealth;

        if (respawnPoint == null)
        {
            Debug.LogError("PlayerVitals on " + name + " has no respawn point; Respawn will stand the player up where they are.", this);
        }
    }

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    void Update()
    {
        if (!IsAlive) return;
        if (Time.time - lastDamageTime < regenDelay) return;
        if (!Generator.IsPointProtected(transform.position)) return;

        health = Mathf.Min(maxHealth, health + regenInSafeZone * Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (!HasAuthority) return;
        if (!IsAlive || amount <= 0f) return;

        health -= amount;
        lastDamageTime = Time.time;
        lastDamageAmount = amount;
        OnDamaged();

        if (health <= 0f)
        {
            health = 0f;
            Die();
        }
    }

    private void Die()
    {
        // Pin the spot before anything reacts: the dropped item and the body both belong here.
        lastDeathPosition = transform.position;
        lastDeathYaw = transform.eulerAngles.y;

        OnDied();
        AnyDied(this);
    }

    /// <summary>
    /// Stand the player up with their feet at <paramref name="feet"/>, facing
    /// <paramref name="yaw"/>. This is the revive: the spot is where the body lay.
    /// A player who is somehow already alive is moved but never has health taken away.
    /// </summary>
    public void ReviveAt(Vector3 feet, float yaw, float healthFraction)
    {
        // The controller's pivot is its centre, not its feet.
        float lift = controller != null
            ? controller.height * 0.5f - controller.center.y + controller.skinWidth
            : 1f;

        StandUp(feet + Vector3.up * lift, yaw, healthFraction);
    }

    /// <summary>Back at the respawn point with full health. The singleplayer fallback's route.</summary>
    public void Respawn()
    {
        if (respawnPoint == null)
        {
            StandUp(transform.position, transform.eulerAngles.y, 1f);
            return;
        }

        StandUp(respawnPoint.position, respawnPoint.rotation.eulerAngles.y, 1f);
    }

    private void StandUp(Vector3 position, float yaw, float healthFraction)
    {
        if (!HasAuthority) return;

        float restored = Mathf.Clamp(maxHealth * healthFraction, 1f, maxHealth);
        health = IsAlive ? Mathf.Max(health, restored) : restored;
        lastDamageTime = Time.time;

        // The controller overwrites transform moves, so switch it off for the teleport --
        // and leave it as it was: whether it is on is PlayerDeathLock's business.
        bool controllerWasOn = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (controller != null) controller.enabled = controllerWasOn;

        Physics.SyncTransforms();

        OnRevived();
        AnyRevived(this);
    }

    void OnGUI()
    {
        if (!IsAlive)
        {
            Hud.Row(3, "Health 0/" + maxHealth.ToString("0") + "   DEAD", new Color(1f, 0.35f, 0.35f));
            return;
        }

        bool safe = Generator.IsPointProtected(transform.position);

        Color tint = HealthNormalized > 0.6f ? Color.white
                   : HealthNormalized > 0.25f ? new Color(1f, 0.72f, 0.30f)
                   : new Color(1f, 0.35f, 0.35f);

        Hud.Row(3, string.Format("Health {0:0}/{1:0}{2}", health, maxHealth, safe ? "   [SAFE]" : ""), tint);
    }
}
