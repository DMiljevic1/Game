using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Puts the night's monsters into the world when the level starts. Level 1 is always
/// night, so the wave is out from the first frame and stays out.
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
    [FormerlySerializedAs("baseCount")]
    public int waveSize = 4;

    [Header("Where")]
    public Vector3 areaCenter = new Vector3(6.5f, 0f, -12f);
    public float areaRadius = 55f;
    [Tooltip("Never spawn closer than this to the player: no appearing in someone's face.")]
    public float minDistanceFromPlayer = 28f;

    [Header("Patrol")]
    [Tooltip("Parent of the patrol points. Its children, in order, become the route " +
             "every spawned monster walks. Leave empty and they wander the area instead.")]
    public Transform patrolRoute;

    private Transform[] routePoints;

    private readonly List<GameObject> spawned = new List<GameObject>();

    // Authority seam, as with Generator and Wallet.
    protected virtual bool HasAuthority { get { return true; } }

    public int AliveCount { get { return spawned.Count; } }

    void Start()
    {
        if (monsterPrefab == null)
        {
            Debug.LogError("MonsterSpawner has no monster prefab assigned.", this);
            return;
        }

        if (HasAuthority) SpawnWave();
    }

    /// <summary>The route's children, cached: the same array is shared by the whole wave.</summary>
    private Transform[] RoutePoints()
    {
        if (patrolRoute == null) return null;
        if (routePoints != null && routePoints.Length == patrolRoute.childCount) return routePoints;

        routePoints = new Transform[patrolRoute.childCount];
        for (int i = 0; i < routePoints.Length; i++) routePoints[i] = patrolRoute.GetChild(i);
        return routePoints;
    }

    public void SpawnWave()
    {
        DespawnAll();

        Transform[] route = RoutePoints();

        int count = waveSize;

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

                // Spread the wave around the loop so they do not walk it in single file.
                if (route != null && route.Length > 0)
                {
                    m.SetPatrolRoute(route, route.Length > 0 ? (i * route.Length) / count : 0);
                }
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

            // The radius, not the running state: the generator starts the level switched off,
            // and a wave spawned in the yard before anyone could start it would be no choice at all.
            if (Generator.IsInsideAnyRadius(ground)) continue;
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
