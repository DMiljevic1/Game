using UnityEngine;

/// <summary>
/// **Singleplayer only -- remove it when co-op lands.**
///
/// With one player, every death is "everyone is dead", and there is no teammate to carry
/// the body home. So instead of ending the run, this stands the player back up at the
/// house after a short wait, to play the part of their own rescuer. The body stays exactly
/// where it fell, so the whole loop -- fetch it, buy adrenaline, revive -- can be played.
///
/// It is the one place singleplayer bends the rules, and it only listens to
/// <see cref="RunState.OnAllPlayersDead"/>. Disable it to see what a dead co-op player
/// sees: locked out, waiting on a body that nobody is coming for.
/// </summary>
[DisallowMultipleComponent]
public class SingleplayerDeathFallback : MonoBehaviour
{
    [Tooltip("Seconds on the death screen before standing back up at the house.")]
    public float standInDelay = 4f;

    [Tooltip("Which run to watch. Falls back to RunState.Instance if left empty.")]
    public RunState runState;

    private float standUpAt = -1f;

    public bool IsPending { get { return standUpAt >= 0f; } }

    // Authority seam: whether a dead team gets back up is a rule, not a view.
    protected virtual bool HasAuthority { get { return true; } }

    void OnEnable()
    {
        if (runState == null) runState = RunState.Instance;
        if (runState != null) runState.OnAllPlayersDead += HandleAllDead;
    }

    void OnDisable()
    {
        if (runState != null) runState.OnAllPlayersDead -= HandleAllDead;
        standUpAt = -1f;
    }

    void Start()
    {
        if (runState == null)
        {
            // OnEnable can run before RunState.Awake has set the Instance; try again now.
            runState = RunState.Instance;
            if (runState != null) runState.OnAllPlayersDead += HandleAllDead;
            else Debug.LogError("SingleplayerDeathFallback on " + name + " has no RunState; a death will never be undone.", this);
        }
    }

    private void HandleAllDead()
    {
        if (!HasAuthority) return;
        standUpAt = Time.time + standInDelay;
    }

    void Update()
    {
        if (!IsPending) return;

        // Someone got revived some other way in the meantime: nothing to do.
        if (runState != null && !runState.AllPlayersDead)
        {
            standUpAt = -1f;
            return;
        }

        if (Time.time < standUpAt) return;
        standUpAt = -1f;

        // Back at the house, not at the body: the body stays in the world to be fetched.
        var all = PlayerVitals.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && !all[i].IsAlive) all[i].Respawn();
        }
    }

    void OnGUI()
    {
        if (!IsPending) return;

        Hud.CentrePrompt(string.Format("Singleplayer: standing in as your own rescuer at the house in {0:0}s",
                                       Mathf.Max(0f, standUpAt - Time.time)));
    }
}
