using UnityEngine;

/// <summary>
/// Player health, and what happens when a monster reaches you.
/// Regenerates only inside generator protection, so the house is where you recover
/// and the field never is.
/// </summary>
[DisallowMultipleComponent]
public class PlayerVitals : MonoBehaviour
{
    public float maxHealth = 100f;
    public float health = 100f;

    [Tooltip("Health per second regained while inside a running generator's radius.")]
    public float regenInSafeZone = 3f;
    [Tooltip("Seconds without damage before regeneration starts.")]
    public float regenDelay = 5f;

    [Tooltip("Where the player reappears after being killed.")]
    public Transform respawnPoint;

    public event System.Action OnDamaged = delegate { };
    public event System.Action OnDied = delegate { };

    private float lastDamageTime = -999f;
    private float lastDamageAmount;
    private Vector3 lastDeathPosition;
    private CharacterController controller;

    public bool IsAlive { get { return health > 0f; } }
    public float HealthNormalized { get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(health / maxHealth); } }

    /// <summary>Size of the most recent hit, so feedback can scale itself without OnDamaged carrying a payload.</summary>
    public float LastDamageAmount { get { return lastDamageAmount; } }

    /// <summary>
    /// Where the player was standing when they were last killed. Recorded before
    /// Respawn moves them, so an OnDied handler can use it without having to know
    /// that it happens to run before the teleport.
    /// </summary>
    public Vector3 LastDeathPosition { get { return lastDeathPosition; } }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = maxHealth;

        if (respawnPoint == null)
        {
            Debug.LogError("PlayerVitals on " + name + " has no respawn point; death will leave the player where they fell.", this);
        }
    }

    void Update()
    {
        if (!IsAlive) return;
        if (Time.time - lastDamageTime < regenDelay) return;
        if (!Generator.IsPointProtected(transform.position)) return;

        health = Mathf.Min(maxHealth, health + regenInSafeZone * Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
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
        // Pin the spot before anything reacts: whatever the player was carrying belongs
        // here, not wherever Respawn is about to put them.
        lastDeathPosition = transform.position;

        OnDied();
        Respawn();
    }

    public void Respawn()
    {
        health = maxHealth;
        lastDamageTime = Time.time;

        if (respawnPoint == null) return;

        // The controller overwrites transform moves, so switch it off for the teleport.
        if (controller != null) controller.enabled = false;
        transform.position = respawnPoint.position;
        transform.rotation = Quaternion.Euler(0f, respawnPoint.rotation.eulerAngles.y, 0f);
        if (controller != null) controller.enabled = true;

        Physics.SyncTransforms();
    }

    void OnGUI()
    {
        bool safe = Generator.IsPointProtected(transform.position);

        Color tint = HealthNormalized > 0.6f ? Color.white
                   : HealthNormalized > 0.25f ? new Color(1f, 0.72f, 0.30f)
                   : new Color(1f, 0.35f, 0.35f);

        Hud.Row(3, string.Format("Health {0:0}/{1:0}{2}", health, maxHealth, safe ? "   [SAFE]" : ""), tint);
    }
}
