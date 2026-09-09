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

    void OnDrawGizmos()
    {
        // Two colours, so a glance down the room says which points can take the big prize.
        Gizmos.color = allowLargeItems
            ? new Color(1f, 0.85f, 0.25f, 0.9f)
            : new Color(0.5f, 0.7f, 1f, 0.9f);

        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p, 0.18f);
        Gizmos.DrawLine(p, p + Vector3.up * 0.45f);
    }
}
