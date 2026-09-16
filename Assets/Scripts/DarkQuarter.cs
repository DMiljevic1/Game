using UnityEngine;

/// <summary>
/// One corner of the map -- a true quarter of the 200 x 200 square, not a wedge -- is very
/// much darker than the rest of the night. Dark enough that you can still just make out the
/// ground and the trunks and not much else, so it is the one part of the level you need a
/// bought flashlight to work in, and the store's cheapest item has somewhere to matter before
/// the UV lamp does.
///
/// The quarter is drawn at the start of every run from the four corners, and **never the
/// corner the Level 2 trail runs into** -- see <see cref="EnsureChosen"/>. Burying a line of
/// eighteen barely-visible footprints in the darkest part of the map would make the only
/// thread the player has a matter of luck.
///
/// Level 1 **names** its corner rather than rolling one, because the mountain is built into
/// one corner of the map and a mountain cannot move between runs. There the darkness is the
/// approach to the mountain rather than weather, and it is the trail that has to keep out of
/// the way -- <see cref="Level2Site"/> asks <see cref="DistanceFromDark"/> and rejects a
/// bearing that would run into it. Set <see cref="fixedCorner"/> back to Roll and the
/// original behaviour comes back untouched, avoidance and all.
///
/// Two things soften it, and both exist so the dark reads as weather rather than as a line
/// painted on the ground: <see cref="edgeSoftness"/> metres of fade across each of the two
/// quadrant boundaries, and a fade out towards the base, so the generator's yard is never in
/// the dark whichever corner is drawn.
///
/// Co-op note: this owns the *choice* -- shared state, one authority, like every other roll.
/// The darkness itself is drawn per viewer by <see cref="NightDepth"/>, which stays the only
/// thing that ever writes ambient or fog.
/// </summary>
[DisallowMultipleComponent]
public class DarkQuarter : MonoBehaviour
{
    public static DarkQuarter Instance { get; private set; }

    /// <summary>Which corner is dark, +z being north and +x east. <see cref="Roll"/> draws one.</summary>
    public enum Quarter { Roll, NorthEast, NorthWest, SouthEast, SouthWest }

    [Header("Which corner")]
    [Tooltip("Roll draws a fresh corner every run from the ones the Level 2 trail does not " +
             "run into. Name a corner instead when something is BUILT there and the darkness " +
             "has to sit on top of it -- the mountain is in one corner of the map and cannot " +
             "move, so the dark quarter cannot either.")]
    public Quarter fixedCorner = Quarter.Roll;

    [Header("The map")]
    [Tooltip("Centre of the playable square -- where the four quarters meet. The ground is built " +
             "on the origin, so that is the default.")]
    public Vector3 mapCentre = Vector3.zero;

    [Tooltip("Half the playable square, matching PrototypeEnvironmentBuilder.GroundHalf. Used only " +
             "to draw the gizmo; the quarter itself is unbounded, so the boundary wall is its edge.")]
    public float mapHalf = 100f;

    [Tooltip("The Level 2 door. Whichever corner it stands in is never the dark one.")]
    public Transform avoid;

    [Header("Where it stops")]
    [Tooltip("What the fade towards safety is measured from: the generator, so the yard is never " +
             "dark whichever corner is drawn.")]
    public Transform centre;

    [Tooltip("Nearer than this to the generator it has no effect at all. The light's own radius, " +
             "so the dark starts exactly where the protection ends.")]
    public float innerRadius = 22f;

    [Tooltip("From here out it is at full strength.")]
    public float fullRadius = 35f;

    [Tooltip("Metres of fade across each quadrant boundary, so walking in is a closing-in rather " +
             "than a step across a line on the ground.")]
    public float edgeSoftness = 12f;

    [Tooltip("A corner is also refused if the Level 2 door is within this of it, so the trail is " +
             "never hard against the dark corner's edge either.")]
    public float clearance = 20f;

    [Header("How dark")]
    [Tooltip("What is left of the ambient light at full strength, on top of the distance gradient " +
             "NightDepth already applies. Retune against captures, never by eye in the Inspector " +
             "-- every albedo in this level is near-black.")]
    [Range(0f, 1f)] public float ambientScale = 0.28f;

    [Tooltip("How much the fog thickens here. Kept modest on purpose: fog dims the flashlight beam " +
             "as much as the moonlight, and this is the one place the flashlight has to work.")]
    public float fogScale = 1.25f;

    [Tooltip("What is left of the moon here. THIS is what makes the corner properly dark: ambient " +
             "alone only takes the light out of the shadows, while the moon goes on picking out " +
             "every surface facing it, so scaling ambient by itself left the woods perfectly " +
             "readable. Cloud over one corner of the sky, in effect.")]
    [Range(0f, 1f)] public float moonScale = 0.05f;

    [Header("Debug")]
    [Tooltip("Logs which corner was drawn when the run starts. It is the only way to know which " +
             "one it is without walking there, and it costs one line.")]
    public bool logChoice = true;

    // Authority seam, as with Expedition and Wallet.
    protected virtual bool HasAuthority { get { return true; } }

    private bool chosen;
    private float signX = -1f;
    private float signZ = 1f;

    /// <summary>Which corner is dark, as a pair of signs on x and z. Readouts and tooling.</summary>
    public Vector2 Corner { get { EnsureChosen(); return new Vector2(signX, signZ); } }

    /// <summary>
    /// True when the corner was named rather than drawn. That is the one case where something
    /// else -- <see cref="Level2Site"/> -- may safely ask where the dark is while it is still
    /// placing itself: a named corner cannot be moved by the asking, so there is no order to
    /// depend on. While it is rolled the dependency runs the other way round and must stay
    /// that way, or the roll happens before the trail has moved.
    /// </summary>
    public bool IsFixed { get { return fixedCorner != Quarter.Roll; } }

    /// <summary>The corner in words, +z being north and +x east.</summary>
    public string CornerName
    {
        get
        {
            EnsureChosen();
            return (signZ > 0f ? "north" : "south") + "-" + (signX > 0f ? "east" : "west");
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second DarkQuarter exists on " + name + "; destroying it. There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Draw the corner, once, the first time anything asks where the dark is.
    ///
    /// Deliberately not in Start: Level2Site swings the trail onto its own random bearing in
    /// *its* Start, and the order two Starts run in is not defined. Asking on the first frame
    /// instead means the trail has certainly been placed -- and a check that silently depends
    /// on execution order is exactly the trap Level2Site.DoorSpotClear documents.
    ///
    /// Every corner the door stands in or near is struck out and one of the rest is drawn, so
    /// the trail cannot be in the dark however the numbers are retuned, and there is no attempt
    /// loop to give up in. Only if every corner were somehow refused does it fall back to the
    /// one furthest from the door, which is still the best answer available.
    /// </summary>
    private void EnsureChosen()
    {
        if (chosen) return;
        chosen = true;

        if (!HasAuthority) return;

        // A named corner is not a roll and has nothing to avoid: something is standing in it.
        if (IsFixed)
        {
            signX = fixedCorner == Quarter.NorthEast || fixedCorner == Quarter.SouthEast ? 1f : -1f;
            signZ = fixedCorner == Quarter.NorthEast || fixedCorner == Quarter.NorthWest ? 1f : -1f;

            if (logChoice)
            {
                Debug.Log("DarkQuarter: the " + CornerName + " quarter of the map is the dark one, " +
                          "and stays the dark one -- it is where the mountain is.", this);
            }
            return;
        }

        Vector2 furthest = new Vector2(-1f, 1f);
        float furthestDistance = -1f;

        Vector2 pick = furthest;
        int eligible = 0;

        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new Vector2((i & 1) == 0 ? 1f : -1f, (i & 2) == 0 ? 1f : -1f);
            float distance = DoorDistanceTo(corner);

            if (distance > furthestDistance)
            {
                furthestDistance = distance;
                furthest = corner;
            }

            if (distance < clearance) continue;      // the trail runs into this one, or close by

            // A reservoir of one: every eligible corner is equally likely, in a single pass.
            eligible++;
            if (Random.Range(0, eligible) == 0) pick = corner;
        }

        if (eligible == 0) pick = furthest;

        signX = pick.x;
        signZ = pick.y;

        if (logChoice)
        {
            Debug.Log("DarkQuarter: the " + CornerName + " quarter of the map is the dark one this run" +
                      (eligible == 0 ? " (no corner was clear of the trail; took the furthest)." : "."), this);
        }
    }

    /// <summary>
    /// How far a point is from the dark corner's ground, 0 meaning it stands in it. Only safe
    /// to ask while <see cref="IsFixed"/> -- see there.
    /// </summary>
    public float DistanceFromDark(Vector3 point)
    {
        EnsureChosen();
        return DistanceTo(new Vector2(signX, signZ), point);
    }

    /// <summary>
    /// How far the Level 2 door is from a corner's ground, 0 meaning it stands in it. A corner
    /// is a quadrant, so the distance is only the part of the offset pointing the wrong way on
    /// each axis.
    /// </summary>
    private float DoorDistanceTo(Vector2 corner)
    {
        if (avoid == null) return Mathf.Infinity;
        return DistanceTo(corner, avoid.position);
    }

    private float DistanceTo(Vector2 corner, Vector3 at)
    {
        Vector3 offset = at - mapCentre;
        float x = Mathf.Max(0f, -corner.x * offset.x);
        float z = Mathf.Max(0f, -corner.y * offset.z);
        return Mathf.Sqrt(x * x + z * z);
    }

    /// <summary>
    /// How much of the dark quarter is in force at this point: 0 outside it or near the house,
    /// 1 well inside it. Pure query -- it changes nothing, so a HUD, a second camera or a
    /// future monster that hunts the dark can all ask it freely.
    /// </summary>
    public float Weight(Vector3 point)
    {
        EnsureChosen();

        Vector3 offset = point - mapCentre;

        // Inside the quadrant on both axes, faded across each boundary rather than switched.
        float soft = Mathf.Max(0.01f, edgeSoftness);
        float here = Smooth(Mathf.Clamp01(signX * offset.x / soft)) *
                     Smooth(Mathf.Clamp01(signZ * offset.z / soft));

        if (here <= 0f) return 0f;

        // ...but never in the generator's yard, whichever corner was drawn.
        if (centre == null) return here;

        Vector3 fromBase = point - centre.position;
        fromBase.y = 0f;

        return here * Smooth(Mathf.InverseLerp(innerRadius, fullRadius, fromBase.magnitude));
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    void OnDrawGizmosSelected()
    {
        // The corner as it stands right now. In edit mode that is whatever was drawn last; the
        // run draws its own.
        Gizmos.color = new Color(0.25f, 0.2f, 0.5f, 0.9f);

        Vector3 a = mapCentre;
        Vector3 b = mapCentre + new Vector3(signX * mapHalf, 0f, 0f);
        Vector3 c = mapCentre + new Vector3(signX * mapHalf, 0f, signZ * mapHalf);
        Vector3 d = mapCentre + new Vector3(0f, 0f, signZ * mapHalf);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);

        if (centre != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(centre.position, fullRadius);
        }
    }
}
