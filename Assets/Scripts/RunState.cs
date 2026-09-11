using UnityEngine;

/// <summary>
/// The single authority on the state of the current run: how many revives have been
/// used, and whether anyone is still alive. It is the same shape as
/// Wallet -- one object, a static Instance, a HasAuthority seam -- and it lives on
/// Systems, so a new level (a new scene) starts a new run for free.
///
/// It decides nothing about what a death or a revive *does*; it only keeps the score.
/// Game over will hang off <see cref="OnAllPlayersDead"/> once there is more than one
/// player to be the last one standing.
///
/// Co-op note: <see cref="revivesUsed"/> becomes a synced variable. Clients mirror it
/// with <see cref="SetRevivesUsed"/> and never count revives themselves.
/// </summary>
[DisallowMultipleComponent]
public class RunState : MonoBehaviour
{
    public static RunState Instance { get; private set; }

    [Tooltip("Revives spent this run, by anyone on anyone. Global for the team, so it is " +
             "what the next Adrenaline is priced from.")]
    public int revivesUsed = 0;

    public event System.Action OnRunStarted = delegate { };
    public event System.Action<int> OnRevivesUsedChanged = delegate { };
    public event System.Action<PlayerVitals> OnPlayerDied = delegate { };
    public event System.Action<PlayerVitals> OnPlayerRevived = delegate { };

    /// <summary>
    /// Nobody is left standing. In co-op this is Game Over; with one player it is every
    /// death, which is why the singleplayer rule is a separate component that listens here.
    /// </summary>
    public event System.Action OnAllPlayersDead = delegate { };

    public int RevivesUsed { get { return revivesUsed; } }

    // Authority seam. Becomes IsServer/IsHost once netcode is in.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>Players currently on their feet.</summary>
    public int AlivePlayerCount
    {
        get
        {
            int alive = 0;
            var all = PlayerVitals.All;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].IsAlive) alive++;
            return alive;
        }
    }

    /// <summary>True when there are players and none of them is alive.</summary>
    public bool AllPlayersDead { get { return PlayerVitals.All.Count > 0 && AlivePlayerCount == 0; } }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second RunState exists on " + name + "; destroying it. There must be exactly one run.", this);
            Destroy(this);
            return;
        }
        Instance = this;
        BeginRun();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        PlayerVitals.AnyDied += HandleDied;
        PlayerVitals.AnyRevived += HandleRevived;
    }

    void OnDisable()
    {
        PlayerVitals.AnyDied -= HandleDied;
        PlayerVitals.AnyRevived -= HandleRevived;
    }

    /// <summary>
    /// Wipe the run. Called on Awake, so every level load is a fresh run; public so a
    /// future "new run" that keeps the scene loaded can call it too.
    /// </summary>
    public void BeginRun()
    {
        if (!HasAuthority) return;

        revivesUsed = 0;
        OnRevivesUsedChanged(revivesUsed);
        OnRunStarted();
    }

    /// <summary>A revive happened. Only <see cref="Revival"/> calls this.</summary>
    public void RecordRevive()
    {
        if (!HasAuthority) return;

        revivesUsed++;
        OnRevivesUsedChanged(revivesUsed);
    }

    /// <summary>
    /// Overwrite the count. Normal play never calls this: it exists so a networked client
    /// can mirror the authority's value, and so the editor can preview a price.
    /// </summary>
    public void SetRevivesUsed(int value)
    {
        value = Mathf.Max(0, value);
        if (value == revivesUsed) return;

        revivesUsed = value;
        OnRevivesUsedChanged(revivesUsed);
    }

    private void HandleDied(PlayerVitals player)
    {
        OnPlayerDied(player);

        // A rule, not a readout: only the authority decides the team is finished.
        if (HasAuthority && AllPlayersDead) OnAllPlayersDead();
    }

    private void HandleRevived(PlayerVitals player)
    {
        OnPlayerRevived(player);
    }
}
