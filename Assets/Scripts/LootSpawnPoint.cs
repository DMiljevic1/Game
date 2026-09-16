using UnityEngine;

/// <summary>
/// A place a piece of loot may be put at the start of a run.
///
/// It is only a marker: it holds no item, no odds and no state, so adding somewhere
/// new to search is dropping one of these in the scene and nothing else. Which item
/// lands here -- if any -- is decided by <see cref="LootSpawner"/>.
///
/// A point is never trusted from its transform alone. The spawner re-probes every one
/// against real geometry, so a point left inside a wall by a house rebuild is skipped
/// rather than swallowing an item.
/// </summary>
[DisallowMultipleComponent]
public class LootSpawnPoint : MonoBehaviour
{
    [Tooltip("Clear this for a spot too cramped or too silly for something the size of " +
             "the television -- a shelf, a windowsill, the top of a fence post. Small and " +
             "medium loot can still appear here.")]
    public bool allowLargeItems = true;

    [Tooltip("Metres to add to how deep this spot counts as, for the depth pairing only. " +
             "Zero for open ground, where the walk out is the whole of the risk. Positive for " +
             "somewhere that is worse than its distance says -- inside the mountain, where the " +
             "walk is the same but you are under rock in the pitch dark. It moves WHICH of the " +
             "drawn pieces lands here and nothing else: the table, the caps and the odds of " +
             "anything at all being here are untouched, so this can never turn a spot into a " +
             "guaranteed payday.")]
    public float extraDepth = 0f;

    void OnDrawGizmos()
    {
        // Two colours, so a glance down the room says which points can take the big prize.
        Gizmos.color = allowLargeItems
            ? new Color(1f, 0.85f, 0.25f, 0.9f)
            : new Color(0.5f, 0.7f, 1f, 0.9f);

        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p, 0.18f);

        // A taller stalk for a spot that counts as deeper than it stands, so a glance down the
        // cave says which of these the dear pieces are drawn towards.
        Gizmos.DrawLine(p, p + Vector3.up * (0.45f + extraDepth * 0.02f));
    }
}
