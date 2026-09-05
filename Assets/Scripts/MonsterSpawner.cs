using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts monsters into the world at dusk and takes them away at dawn, so "they don't
/// come during the day" is true in the simulation before any journal page says it.
///
/// Co-op note: spawning is authority-only. Clients will receive spawned monsters
/// through netcode rather than running this themselves.
/// </summary>
[DisallowMultipleComponent]
public class MonsterSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    public GameObject monsterPrefab;
    public Transform player;

    [Header("How many")]
    public int baseCount = 4;
    [Tooltip("Extra monsters added per day survived, so pressure grows.")]
    public int extraPerDay = 1;
    public int maxCount = 12;

    [Header("Where")]
    public Vector3 areaCenter = new Vector3(6.5f, 0f, -12f);
    public float areaRadius = 55f;
    [Tooltip("Never spawn closer than this to the player: no appearing in someone's face.")]
    public float minDistanceFromPlayer = 28f;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private TimeOfDay clock;

    // Authority seam, as with TimeOfDay and Generator.
    protected virtual bool HasAuthority { get { return true; } }

    public int AliveCount { get { return spawned.Count; } }

    void Start()
    {
        clock = TimeOfDay.Instance;
        if (clock == null)
        {
            Debug.LogError("MonsterSpawner found no TimeOfDay; monsters will never spawn.", this);
            return;
        }
        if (monsterPrefab == null)
        {
            Debug.LogError("MonsterSpawner has no monster prefab assigned.", this);
            return;
        }

        clock.OnPhaseChanged += HandlePhaseChanged;
    }

    void OnDestroy()
    {
        if (clock != null) clock.OnPhaseChanged -= HandlePhaseChanged;
    }

    private void HandlePhaseChanged(DayPhase previous, DayPhase current)
    {
        if (!HasAuthority) return;

        // Dusk, not night: they should already be out there when the light goes.
        if (current == DayPhase.Dusk) SpawnWave();
        else if (current == DayPhase.Dawn) DespawnAll();
    }

    public void SpawnWave()
    {
        DespawnAll();

        int count = Mathf.Min(maxCount, baseCount + extraPerDay * (clock.DayNumber - 1));

        for (int i = 0; i < count; i++)
        {
            Vector3 p;
            if (!TryFindSpawnPoint(out p)) continue;

            GameObject go = Instantiate(monsterPrefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.name = "Monster_" + (i + 1);

            Monster m = go.GetComponent<Monster>();
            if (m != null)
            {
                m.SetTarget(player);
                m.roamCenter = areaCenter;
                m.roamRadius = areaRadius;
            }

            spawned.Add(go);
        }
    }

    public void DespawnAll()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null) Destroy(spawned[i]);
        }
        spawned.Clear();
    }

    private bool TryFindSpawnPoint(out Vector3 point)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 c = Random.insideUnitCircle * areaRadius;
            Vector3 candidate = new Vector3(areaCenter.x + c.x, areaCenter.y + 40f, areaCenter.z + c.y);

            RaycastHit hit;
            if (!Physics.Raycast(candidate, Vector3.down, out hit, 80f)) continue;

            Vector3 ground = hit.point + Vector3.up * 1.1f;

            if (Generator.IsPointProtected(ground)) continue;
            if (player != null && Vector3.Distance(ground, player.position) < minDistanceFromPlayer) continue;

            point = ground;
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    void OnGUI()
    {
        Hud.Row(4, "Monsters: " + spawned.Count,
                spawned.Count > 0 ? new Color(1f, 0.55f, 0.45f) : Color.white);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(areaCenter, areaRadius);
    }
}
