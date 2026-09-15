using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Decides where a monster wanders when nothing has its attention.
///
/// It knows nothing about hearing, sight, agitation or the player -- hand it a position,
/// get back a walkable NavMesh point far enough away to be a real trip. That ignorance is
/// the point: the blind monster uses it today and a sighted one can use it tomorrow
/// without either of them sharing a line of detection code. Detection always outranks it;
/// a monster simply stops asking while it is chasing something.
///
/// The feel it is tuned for is "out walking", not "waiting to be triggered": long legs
/// across the map, a heading that carries on rather than doubling back, and a pause only
/// now and then rather than at every stop.
///
/// <see cref="Bias"/> is the one thing detection may say to it, and it is deliberately
/// vague: "keep your round near here for a while". Still no mention of the player, and
/// the leash widens back out to the whole area on its own, so nothing has to remember to
/// switch it off.
/// </summary>
[DisallowMultipleComponent]
public class MonsterPatrol : MonoBehaviour
{
    [Header("Where it may roam")]
    [Tooltip("Centre of the area it wanders. MonsterSpawner pushes the level's area in.")]
    public Vector3 areaCenter = Vector3.zero;
    public float areaRadius = 45f;

    [Header("How far it goes")]
    [Tooltip("A new destination is never nearer than this, so it cannot dither in one corner.")]
    public float minTravelDistance = 16f;
    public float maxTravelDistance = 45f;
    [Tooltip("Degrees either side of the direction it is already travelling that a new leg " +
             "may set off in. Under 180 keeps it moving on rather than turning round on the spot.")]
    [Range(20f, 180f)] public float directionSpread = 130f;
    [Tooltip("How far a rough candidate point may be snapped to find the walkable surface.")]
    public float sampleRadius = 5f;
    [Tooltip("Tries per choice. Each one relaxes the distance and heading a little, so a " +
             "monster boxed into a small space still finds somewhere legal to go.")]
    public int maxAttempts = 20;

    [Header("Pauses")]
    [Tooltip("Chance of stopping at all on arrival. Most arrivals should roll straight on.")]
    [Range(0f, 1f)] public float pauseChance = 0.3f;
    public float minPauseTime = 0.4f;
    public float maxPauseTime = 2.2f;

    [Header("Searching near a point")]
    [Tooltip("Closest a ChooseNear point may be to where the searcher is standing, so a search " +
             "step is always a walk to somewhere else rather than a shuffle in place.")]
    public float minSearchStep = 3f;

    [Header("Anchors (optional)")]
    [Tooltip("Places worth visiting -- MonsterSpawner fills this from PatrolRoute. They are a " +
             "bias, never a loop: most legs are still random, so a wave never walks in single file.")]
    public Transform[] anchors;
    [Range(0f, 1f)] public float anchorChance = 0.25f;

    [Header("Debug")]
    public bool showDebug = true;

    // Built in Awake, never in a field initializer: Unity forbids creating a NavMeshPath
    // while a MonoBehaviour is being constructed.
    private NavMeshPath probe;
    private Vector3 heading = Vector3.forward;
    private Vector3 destination;
    private bool hasDestination;
    private bool everChosen;
    private bool warnedNoNavMesh;

    // A temporary, decaying leash on where the round may go. Detection sets it; nothing
    // has to clear it.
    private Vector3 biasCentre;
    private float biasRadius;
    private float biasTimer;
    private float biasDuration;

    public bool HasDestination { get { return hasDestination; } }
    public Vector3 Destination { get { return destination; } }

    /// <summary>True while the round is still being held near a <see cref="Bias"/> point.</summary>
    public bool IsBiased { get { return biasTimer > 0f; } }

    /// <summary>Seconds of leash left. For readouts only.</summary>
    public float BiasTimeLeft { get { return Mathf.Max(0f, biasTimer); } }

    public Vector3 BiasCentre { get { return biasCentre; } }

    /// <summary>
    /// Where the round is allowed to go right now. Under a bias this starts small and
    /// grows back to the full area as the leash runs out, so the monster drifts away from
    /// the area rather than snapping out of it.
    /// </summary>
    public Vector3 AreaCentre
    {
        get
        {
            if (!IsBiased) return areaCenter;
            return Vector3.Lerp(areaCenter, biasCentre, biasTimer / Mathf.Max(0.01f, biasDuration));
        }
    }

    public float AreaRadius
    {
        get
        {
            if (!IsBiased) return areaRadius;
            return Mathf.Lerp(areaRadius, biasRadius, biasTimer / Mathf.Max(0.01f, biasDuration));
        }
    }

    /// <summary>A leg has to fit inside the leash, so the floor comes down with the radius.</summary>
    private float MinTravel
    {
        get { return Mathf.Min(minTravelDistance, AreaRadius * 0.6f); }
    }

    void Awake()
    {
        probe = new NavMeshPath();
        heading = transform.forward;
    }

    void Update()
    {
        if (biasTimer > 0f) biasTimer -= Time.deltaTime;
    }

    /// <summary>
    /// Keep the round near a point for a while -- what a monster does with a place that
    /// interested it and then went quiet. The leash widens back to the full area over
    /// <paramref name="seconds"/>, so it is a fading preference rather than a cage, and a
    /// second call simply takes over from the first.
    /// </summary>
    public void Bias(Vector3 centre, float radius, float seconds)
    {
        if (seconds <= 0f || radius <= 0f) return;

        biasCentre = centre;
        biasRadius = radius;
        biasDuration = seconds;
        biasTimer = seconds;

        Forget();   // start a leg inside the new leash rather than finishing the old one
    }

    /// <summary>
    /// A walkable point within <paramref name="radius"/> of <paramref name="centre"/> that
    /// can actually be reached from <paramref name="from"/> -- one step of a search around a
    /// noise. A pure query: it does not touch the patrol's own leg, so a monster can search
    /// and still have a round to go back to.
    /// </summary>
    public bool ChooseNear(Vector3 centre, float radius, Vector3 from, out Vector3 point)
    {
        if (probe == null) probe = new NavMeshPath();
        point = centre;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Later attempts accept somewhere closer: in a small room or a tight stand of
            // trees there may be nothing further out at all.
            float relax = maxAttempts <= 1 ? 1f : attempt / (float)(maxAttempts - 1);
            float wantStep = Mathf.Lerp(minSearchStep, 0f, relax);

            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = centre + new Vector3(offset.x, 0f, offset.y);

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, sampleRadius, NavMesh.AllAreas)) continue;
            if (FlatDistance(from, hit.position) < wantStep) continue;
            if (Generator.IsPointProtected(hit.position)) continue;   // never search into the light

            if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, probe)) continue;
            if (probe.status != NavMeshPathStatus.PathComplete) continue;

            point = hit.position;
            return true;
        }

        return false;
    }

    /// <summary>How long to stand still on arrival. Usually not at all.</summary>
    public float NextPauseTime()
    {
        return Random.value < pauseChance ? Random.Range(minPauseTime, maxPauseTime) : 0f;
    }

    /// <summary>Throw away the current leg so the next call picks a fresh one.</summary>
    public void Forget()
    {
        hasDestination = false;
    }

    /// <summary>
    /// Choose somewhere new to walk. Returns false only when nothing legal could be found
    /// at all, which the caller should treat as "try again next frame", not as an error.
    /// </summary>
    public bool ChooseDestination(Vector3 from)
    {
        if (probe == null) probe = new NavMeshPath();   // also lets editor tooling drive this

        Vector3 previous = destination;
        bool hadPrevious = everChosen;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Later attempts want it less: shorter legs, any direction. A monster shut in a
            // small room still gets out rather than standing there failing perfectly.
            float relax = maxAttempts <= 1 ? 1f : attempt / (float)(maxAttempts - 1);
            float minTravel = MinTravel;
            float wantDistance = Mathf.Lerp(minTravel, minTravel * 0.35f, relax);
            float spread = Mathf.Lerp(directionSpread, 180f, relax);

            Vector3 candidate = (anchors != null && anchors.Length > 0 && Random.value < anchorChance)
                              ? AnchorCandidate()
                              : WanderCandidate(from, spread, wantDistance);

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, sampleRadius, NavMesh.AllAreas)) continue;
            if (FlatDistance(from, hit.position) < wantDistance) continue;
            if (Generator.IsPointProtected(hit.position)) continue;          // never wander into the light

            // Not straight back to the one just finished: that pacing is what we are killing.
            if (hadPrevious && FlatDistance(hit.position, previous) < minTravel * 0.5f) continue;

            // The whole reason for a NavMesh: only take a destination it can actually walk to.
            if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, probe)) continue;
            if (probe.status != NavMeshPathStatus.PathComplete) continue;

            Accept(from, hit.position);
            return true;
        }

        // Nothing found and we are not even standing on a mesh: nothing has been baked, so
        // fall back to the old blind circle rather than freezing the whole patrol.
        NavMeshHit under;
        if (!NavMesh.SamplePosition(from, out under, sampleRadius, NavMesh.AllAreas))
        {
            if (!warnedNoNavMesh)
            {
                Debug.LogWarning("MonsterPatrol found no NavMesh under " + name + "; roaming in " +
                                 "straight lines instead. Bake the NavMeshSurface on Systems.", this);
                warnedNoNavMesh = true;
            }

            float distance = Random.Range(MinTravel, maxTravelDistance);
            Accept(from, ClampToArea(from + RandomHeading(from, 180f) * distance));
            return true;
        }

        return false;
    }

    private void Accept(Vector3 from, Vector3 point)
    {
        Vector3 travel = point - from;
        travel.y = 0f;
        if (travel.sqrMagnitude > 0.01f) heading = travel.normalized;   // carry the direction into the next leg

        destination = point;
        everChosen = true;
        hasDestination = true;
    }

    private Vector3 WanderCandidate(Vector3 from, float spread, float minDistance)
    {
        Vector3 direction = RandomHeading(from, spread);
        float distance = Random.Range(minDistance, Mathf.Max(minDistance + 1f, maxTravelDistance));
        return ClampToArea(from + direction * distance);
    }

    private Vector3 RandomHeading(Vector3 from, float spread)
    {
        Vector3 basis = heading;

        // Near the rim, aim back inwards rather than repeatedly off the edge of the world.
        Vector3 inward = AreaCentre - from;
        inward.y = 0f;
        if (inward.magnitude > AreaRadius * 0.75f && inward.sqrMagnitude > 0.01f)
            basis = inward.normalized;

        if (basis.sqrMagnitude < 0.01f) basis = Vector3.forward;
        return Quaternion.Euler(0f, Random.Range(-spread, spread), 0f) * basis;
    }

    private Vector3 AnchorCandidate()
    {
        // Clamped like any other candidate, so an anchor across the map cannot slip a leg
        // out of a bias leash that is meant to hold the round near one place.
        Transform t = anchors[Random.Range(0, anchors.Length)];
        return t == null ? AreaCentre : ClampToArea(t.position);
    }

    private Vector3 ClampToArea(Vector3 point)
    {
        Vector3 centre = AreaCentre;

        Vector3 offset = point - centre;
        offset.y = 0f;

        float limit = AreaRadius * 0.95f;
        if (offset.magnitude <= limit) return point;

        Vector3 clamped = centre + offset.normalized * limit;
        return new Vector3(clamped.x, point.y, clamped.z);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebug) return;

        Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.4f);
        Gizmos.DrawWireSphere(areaCenter, areaRadius);

        // The leash, if one is on: where the round is being held for now.
        if (IsBiased)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(AreaCentre, AreaRadius);
        }

        if (!hasDestination) return;
        Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.9f);
        Gizmos.DrawWireSphere(destination + Vector3.up * 0.2f, 0.6f);
    }
}
