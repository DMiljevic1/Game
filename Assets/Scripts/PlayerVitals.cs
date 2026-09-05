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
    private CharacterController controller;

    public bool IsAlive { get { return health > 0f; } }
    public float HealthNormalized { get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(health / maxHealth); } }

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
        OnDamaged();

        if (health <= 0f)
        {
            health = 0f;
            Die();
        }
    }

    private void Die()
    {
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
