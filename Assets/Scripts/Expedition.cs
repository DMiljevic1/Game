using UnityEngine;

/// <summary>
/// Level 1's objective and nothing else: it puts Tom's case out at his camp, and knows
/// whether it has been opened at home. There is deliberately no quest state beyond that --
/// no stages, no markers, no timer. Where the case is and who is carrying it are already
/// answered by the case itself.
///
/// Co-op note: one authority object owning one piece of shared state, the same shape as
/// Wallet and RunState. Only the authority spawns the case and completes the level.
/// </summary>
[DisallowMultipleComponent]
public class Expedition : MonoBehaviour
{
    public static Expedition Instance { get; private set; }

    [Header("Tom's case")]
    [Tooltip("Spawned at the camp when the level starts. Needs a TomsCase.")]
    public GameObject casePrefab;

    [Tooltip("Where Tom's camp is. Placed and wired by the environment builder.")]
    public Transform campPoint;

    /// <summary>Fired once, when the case is opened at home.</summary>
    public event System.Action OnLevelComplete = delegate { };

    private TomsCase spawnedCase;
    private bool complete;

    public bool IsComplete { get { return complete; } }

    /// <summary>The case in the world, or null before it has been spawned.</summary>
    public TomsCase Case { get { return spawnedCase; } }

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
        if (HasAuthority) SpawnCase();
    }

    private void SpawnCase()
    {
        if (casePrefab == null || campPoint == null)
        {
            Debug.LogError("Expedition has no case prefab or camp point; Level 1 cannot be finished.", this);
            return;
        }

        GameObject go = Instantiate(casePrefab, campPoint.position, campPoint.rotation);
        go.name = casePrefab.name;

        spawnedCase = go.GetComponent<TomsCase>();
        if (spawnedCase == null)
        {
            Debug.LogError("Expedition's case prefab has no TomsCase; Level 1 cannot be finished.", this);
        }
    }

    /// <summary>The case has been opened at home. Only <see cref="CaseTable"/> calls this.</summary>
    public void CompleteLevel()
    {
        if (complete || !HasAuthority) return;

        complete = true;
        OnLevelComplete();
    }
}
