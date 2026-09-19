using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Game over: nobody is left standing, so the run ends and the level starts again.
///
/// It listens to <see cref="RunState.OnAllPlayersDead"/> and nothing else, which is what makes
/// the player count irrelevant to it. RunState fires that event from the death of whoever
/// happens to be the last one on their feet -- so with three players it is the third death and
/// with one player it is the only death, and this component cannot tell the difference. That is
/// the point: there is no singleplayer branch here to drift out of step with the co-op one.
///
/// **It owns no state to put back, and must never grow any.** Everything a run accumulates --
/// the Wallet, RunState's revive count, the generator's fuel, the loot roll, where the key
/// fragments are, which bearing the blood trail is on, which corner is dark -- is scene state,
/// rolled or reset in Awake/Start. So reloading the scene *is* the reset, and there is
/// deliberately no "undo the run" code for that state to disagree with.
///
/// Co-op note: authority-owned, like every other rule. What changes when netcode lands is only
/// who calls it -- the server decides the team is finished and everyone reloads.
/// </summary>
[DisallowMultipleComponent]
public class RunReset : MonoBehaviour
{
    [Tooltip("Seconds on the game over message before the level restarts.")]
    public float restartDelay = 4f;

    [Tooltip("Which run to watch. Falls back to RunState.Instance if left empty.")]
    public RunState runState;

    [Tooltip("Shown centred while the countdown runs.")]
    public string message = "GAME OVER";

    private float restartAt = -1f;

    /// <summary>True while the countdown is running.</summary>
    public bool IsPending { get { return restartAt >= 0f; } }

    // Authority seam: whether the run is over is a rule, not a view.
    protected virtual bool HasAuthority { get { return true; } }

    void OnEnable()
    {
        if (runState == null) runState = RunState.Instance;
        if (runState != null) runState.OnAllPlayersDead += HandleAllDead;
    }

    void OnDisable()
    {
        if (runState != null) runState.OnAllPlayersDead -= HandleAllDead;
        restartAt = -1f;
    }

    void Start()
    {
        if (runState == null)
        {
            // OnEnable can run before RunState.Awake has set the Instance; try again now.
            runState = RunState.Instance;
            if (runState != null) runState.OnAllPlayersDead += HandleAllDead;
            else Debug.LogError("RunReset on " + name + " has no RunState; a wiped team will never restart.", this);
        }
    }

    private void HandleAllDead()
    {
        if (!HasAuthority || IsPending) return;

        restartAt = Time.time + Mathf.Max(0f, restartDelay);
    }

    void Update()
    {
        if (!IsPending) return;

        // Somebody is back on their feet after all. Unreachable today -- a revive needs a living
        // rescuer -- but the countdown has to be cancellable rather than fire late if anything
        // ever stands a player up, and that is cheaper than assuming nothing will.
        if (runState != null && !runState.AllPlayersDead)
        {
            restartAt = -1f;
            return;
        }

        if (Time.time < restartAt) return;
        restartAt = -1f;

        // The cursor is only free while the store is open, but dying with it open is possible,
        // and a fresh level with a loose cursor and no mouse look is the worst thing to leave
        // behind. Same reasoning as Store.Update's safety close.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
    }

    void OnGUI()
    {
        if (!IsPending) return;

        Hud.CentrePrompt(string.Format("{0}  -  restarting in {1:0}s",
                                       message, Mathf.Max(0f, restartAt - Time.time)));
    }
}
