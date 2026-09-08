using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Turns "go over there" into "walk at this corner next". It owns a NavMeshPath and
/// nothing else -- the caller keeps doing its own moving, so a monster still travels on
/// its CharacterController with its own gravity, its own safe-zone push-out and its own
/// attack-by-touch. This is the routing only.
///
/// It is deliberately forgiving: if either end is off the mesh (or there is no baked
/// mesh at all) it hands back the destination unchanged, which is exactly the straight
/// steering the monsters used before. Nothing breaks without a bake, it just gets dumber.
///
/// <see cref="Blocked"/> is the useful signal: true only when both ends sampled fine and
/// the route still came back incomplete -- i.e. "you cannot get there from here", which
/// is a reason to pick somewhere else rather than to keep walking into a wall.
/// </summary>
public class NavPathFollower
{
    [Tooltip("Seconds between recalculations. The world moves (doors, the generator), so a path goes stale.")]
    public float repathInterval = 0.5f;
    [Tooltip("How close counts as having rounded a corner.")]
    public float cornerTolerance = 0.7f;
    [Tooltip("A destination that moves further than this is a new trip, not a nudge.")]
    public float destinationDrift = 1f;
    [Tooltip("How far off the mesh an endpoint may be and still snap onto it.")]
    public float sampleRadius = 3f;

    // Built on first use, never in a field initializer: Unity forbids creating a
    // NavMeshPath while a MonoBehaviour is being constructed, and a monster owns one of these.
    private NavMeshPath path;
    private readonly Vector3[] corners = new Vector3[64];   // reused, so following a path allocates nothing
    private int cornerCount;
    private int cornerIndex;

    private Vector3 destination;
    private bool hasDestination;
    private float nextRepathTime;
    private bool blocked;

    /// <summary>True when a route was genuinely computed and does not reach the destination.</summary>
    public bool Blocked { get { return blocked; } }

    /// <summary>Corners still ahead of us, for debug drawing.</summary>
    public int RemainingCorners { get { return Mathf.Max(0, cornerCount - cornerIndex); } }

    /// <summary>Forget the current route. Call when the trip is abandoned.</summary>
    public void Clear()
    {
        hasDestination = false;
        cornerCount = 0;
        cornerIndex = 0;
        nextRepathTime = 0f;
        blocked = false;
    }

    /// <summary>
    /// The point to steer at this frame on the way to <paramref name="to"/>.
    /// Recalculates when the destination has really moved or the path has gone stale.
    /// </summary>
    public Vector3 Steer(Vector3 from, Vector3 to)
    {
        bool retargeted = !hasDestination || FlatSqrDistance(to, destination) > destinationDrift * destinationDrift;
        if (retargeted || Time.time >= nextRepathTime) Repath(from, to);

        // Drop the corners already rounded. A jump (the safe-zone push-out) can skip several.
        while (cornerIndex < cornerCount &&
               FlatSqrDistance(from, corners[cornerIndex]) < cornerTolerance * cornerTolerance)
            cornerIndex++;

        return cornerIndex < cornerCount ? corners[cornerIndex] : to;
    }

    private void Repath(Vector3 from, Vector3 to)
    {
        destination = to;
        hasDestination = true;
        nextRepathTime = Time.time + repathInterval;
        cornerCount = 0;
        cornerIndex = 0;
        blocked = false;

        if (path == null) path = new NavMeshPath();

        NavMeshHit start, end;
        if (!NavMesh.SamplePosition(from, out start, sampleRadius, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(to, out end, sampleRadius, NavMesh.AllAreas)) return;

        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path))
        {
            blocked = true;
            return;
        }

        blocked = path.status != NavMeshPathStatus.PathComplete;
        cornerCount = path.GetCornersNonAlloc(corners);

        // The first corner is where we already are; steering at it would stall us.
        if (cornerCount > 1) cornerIndex = 1;
    }

    /// <summary>Draws the route being followed. Pure observation, costs nothing when unused.</summary>
    public void DrawDebug(Vector3 from, Color color)
    {
        Vector3 previous = from;
        for (int i = cornerIndex; i < cornerCount; i++)
        {
            Debug.DrawLine(previous + Vector3.up * 0.4f, corners[i] + Vector3.up * 0.4f, color);
            previous = corners[i];
        }
    }

    private static float FlatSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
