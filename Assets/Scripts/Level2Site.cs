using UnityEngine;

/// <summary>
/// Swings the whole UV trail -- footprints, circle and door -- onto a fresh bearing at the
/// start of every run, and sits every piece of it down on the real ground.
///
/// The environment builder lays the site out in a straight line along the root's local +z,
/// from the near woods out to the edge of the map. This rotates that root about the house,
/// so **which direction you have to walk to find the trail changes every run** while the
/// shape of it -- a line of steps leading outward to a mark -- stays exactly as authored.
///
/// That matters more than it looks: a fixed trail is learned once and then walked straight
/// to, which is the failure the whole level was rebuilt to avoid. Rotating is the cheapest
/// possible way to keep the discovery real without randomising authored geometry.
///
/// Co-op note: authority-only, like every other roll. Clients receive the placed transforms.
/// </summary>
[DisallowMultipleComponent]
public class Level2Site : MonoBehaviour
{
    [Tooltip("The door at the far end. Its spot has to be clear, or the bearing is rejected.")]
    public Level2Door door;

    [Tooltip("Bearings to try before giving up and taking the last one anyway.")]
    public int attempts = 40;

    [Tooltip("Clear space the door needs, so it never ends up inside a trunk or a rock.")]
    public Vector3 doorClearance = new Vector3(2.2f, 2.4f, 2.2f);

    [Tooltip("How far above the sampled point the ground is searched for.")]
    public float probeHeight = 40f;

    [Tooltip("Print which way the trail runs when the level starts. The bearing is re-rolled " +
             "every run, so without this there is no way to know but to walk a full circle.")]
    public bool logBearing = true;

    [Tooltip("Negative rolls a fresh bearing every run, which is the shipping behaviour. Set a " +
             "compass angle (0 = north, 90 = east, 180 = south) to pin the trail in one place " +
             "so it can be walked to while testing.")]
    public float fixedBearing = -1f;

    // Authority seam, as with Wallet and Expedition.
    protected virtual bool HasAuthority { get { return true; } }

    void Start()
    {
        if (!HasAuthority) return;

        ChooseBearing();
        SitOnGround();
        LogBearing();
    }

    private static readonly string[] Compass =
    {
        "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"
    };

    /// <summary>
    /// Say which way the trail runs this time.
    ///
    /// The bearing moves every run, which is the whole point -- but it also means a tester has
    /// no way to tell a trail that is pointing somewhere else from a trail that is broken, and
    /// "it was there last time, same place" is the symptom of the design working. One line in
    /// the console settles it. Same reasoning as <see cref="DarkQuarter"/>'s logChoice, which
    /// prints its corner for exactly the same reason.
    /// </summary>
    private void LogBearing()
    {
        if (!logBearing) return;

        float bearing = transform.eulerAngles.y;

        // Deliberately a warning rather than a log, purely so it cannot be scrolled past in a
        // busy console -- this exists to be read. Clear logBearing when you stop testing.
        Debug.LogWarning(string.Format(
            "Blood trail runs {0} from the house this run ({1:0}°); first mark about {2:0} m out. " +
            "Buy the UV flashlight, press X, walk that way and sweep the ground.",
            Compass[Mathf.RoundToInt(bearing / 45f) % 8], bearing,
            transform.childCount > 0 ? Vector3.Distance(transform.position, transform.GetChild(0).position) : 0f),
            this);
    }

    /// <summary>
    /// Turn the site to a random bearing, rejecting any that would bury the door. The map is
    /// forest nearly all the way round, so a rejection means that one direction is crowded
    /// rather than that the rules are wrong -- hence taking the last try rather than failing.
    /// </summary>
    private void ChooseBearing()
    {
        Physics.SyncTransforms();

        // Pinned for testing. It still has to pass the same checks as a rolled one -- a fixed
        // bearing that buries the door would be worse than a moving one, not better -- so a
        // refusal falls through to the roll rather than pretending it worked.
        if (fixedBearing >= 0f)
        {
            transform.rotation = Quaternion.Euler(0f, fixedBearing, 0f);
            Physics.SyncTransforms();

            if (door == null || BearingClear()) return;

            Debug.LogWarning("Level2Site: fixedBearing " + fixedBearing.ToString("0") +
                             "° is blocked, rolling instead.", this);
        }

        for (int i = 0; i < Mathf.Max(1, attempts); i++)
        {
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Physics.SyncTransforms();

            if (door == null) return;
            if (BearingClear()) return;
        }

        Debug.LogWarning("Level2Site could not find a clear bearing for the door; using the last one.", this);
    }

    /// <summary>
    /// Is this whole bearing usable? Three questions, cheapest first.
    ///
    /// The door's own spot has always had to be clear. The other two exist because the map now
    /// has a mountain in one corner and the dark quarter sits on top of it:
    ///
    ///   * **Not into the dark corner.** A line of eighteen barely-visible footprints in the
    ///     blackest part of the level would make the only thread the player has a matter of
    ///     luck. While the corner is rolled that is <see cref="DarkQuarter"/>'s job and asking
    ///     it here would force its roll before the trail had moved; while it is *named* there
    ///     is no roll to force, so the check belongs on this side. Hence the IsFixed guard.
    ///   * **The marks can reach real ground.** A mark that lands in solid rock is one the
    ///     player can never find, and on a trail of eighteen every gap is a gap in the only
    ///     thread there is. A handful of nudged marks is normal and always was; a bearing that
    ///     loses more than <see cref="lostMarksAllowed"/> of them is pointing into something.
    /// </summary>
    private bool BearingClear()
    {
        if (!DoorSpotClear()) return false;

        DarkQuarter dark = DarkQuarter.Instance;
        if (dark != null && dark.IsFixed &&
            dark.DistanceFromDark(door.transform.position) < dark.clearance)
        {
            return false;
        }

        return MarksCanReachGround();
    }

    [Tooltip("How many marks may fail to find ground before the whole bearing is rejected. A " +
             "few nudged marks are normal; a trail with a hole in it is not.")]
    public int lostMarksAllowed = 2;

    /// <summary>
    /// A dry run of <see cref="SitOnGround"/>: would every mark find somewhere to sit, at its
    /// own spot or a nudge away? Nothing is moved -- this only counts the ones that could not.
    /// </summary>
    private bool MarksCanReachGround()
    {
        int lost = 0;

        foreach (Transform child in transform)
        {
            if (CanSit(child.position)) continue;

            if (++lost > lostMarksAllowed) return false;
        }
        return true;
    }

    /// <summary>Is there ground at this spot, or within a nudge of it?</summary>
    private bool CanSit(Vector3 at)
    {
        Vector3 seat;
        bool nudged;
        return TryFindSeat(at, out seat, out nudged);
    }

    /// <summary>
    /// Where a mark dropped at this spot would end up: the ground beneath it, or the ground a
    /// small spiral of <see cref="nudgeRadius"/> away if something is standing on it. One
    /// definition, used both to place the marks and to judge a bearing before committing to it,
    /// so what is tested and what happens cannot drift apart.
    /// </summary>
    private bool TryFindSeat(Vector3 at, out Vector3 seat, out bool nudged)
    {
        nudged = false;

        Vector3 found;
        if (GroundAt(at, out found)) { seat = new Vector3(at.x, found.y, at.z); return true; }

        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            Vector3 side = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * nudgeRadius;

            if (!GroundAt(side, out found)) continue;

            seat = new Vector3(side.x, found.y, side.z);
            nudged = true;
            return true;
        }

        seat = at;
        return false;
    }

    // Reused by the clearance test, so choosing a bearing allocates nothing.
    private readonly Collider[] overlap = new Collider[32];

    /// <summary>
    /// Is there room for the door here?
    ///
    /// It filters out the site's own colliders explicitly rather than trusting that
    /// <see cref="Level2Door"/> has already switched them off. That ordering does hold today
    /// (every Awake runs before any Start), but a test that silently passes or fails on
    /// script execution order is a trap -- and this one would fail *closed*, rejecting every
    /// bearing in the level and quietly falling back to the authored one.
    /// </summary>
    private bool DoorSpotClear()
    {
        Vector3 at = door.transform.position + Vector3.up * (doorClearance.y * 0.5f + 0.2f);

        int count = Physics.OverlapBoxNonAlloc(at, doorClearance * 0.5f, overlap,
                                               door.transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (overlap[i] == null) continue;
            if (overlap[i].transform.IsChildOf(transform)) continue;   // the door and its frame
            return false;
        }
        return true;
    }

    [Tooltip("How far a mark may be nudged sideways to get out of a trunk or a rock.")]
    public float nudgeRadius = 1.6f;

    [Tooltip("The root whose surface counts as walkable ground. Anything else the ray lands " +
             "on is something in the way.")]
    public string groundRootName = "Ground";

    /// <summary>
    /// Drop every mark onto the ground, stepping it aside if it would land on top of a trunk
    /// or a rock.
    ///
    /// The bearing is rolled after the forest exists, so a mark can easily come down inside a
    /// tree -- and a footprint buried in a trunk is one the player can never find, which on a
    /// trail of eighteen is a gap in the only thread they have. The nudge is a small spiral
    /// rather than a re-roll, so the line still reads as a line.
    /// </summary>
    private void SitOnGround()
    {
        int moved = 0, lost = 0;

        foreach (Transform child in transform)
        {
            Vector3 seat;
            bool nudged;

            if (!TryFindSeat(child.position, out seat, out nudged)) { lost++; continue; }
            if (nudged) moved++;

            child.position = new Vector3(seat.x, seat.y + child.localPosition.y, seat.z);
        }

        if (moved > 0 || lost > 0)
        {
            Debug.Log(string.Format("Level2Site: {0} marks nudged clear of the trees, {1} left where they were.",
                                    moved, lost), this);
        }
    }

    /// <summary>The ground under a point, or false if something is standing on it.</summary>
    private bool GroundAt(Vector3 at, out Vector3 point)
    {
        point = at;

        RaycastHit hit;
        if (!Physics.Raycast(at + Vector3.up * probeHeight, Vector3.down, out hit,
                             probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // Anything belonging to the site is skipped, or the door's own frame could catch the
        // ray meant for the ground beneath it.
        if (hit.transform.IsChildOf(transform)) return false;

        // Only the ground itself counts. A trunk, a rock or a wall means this spot is taken.
        if (hit.transform.root.name != groundRootName) return false;

        point = hit.point;
        return true;
    }
}
