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

    // Authority seam, as with Wallet and Expedition.
    protected virtual bool HasAuthority { get { return true; } }

    void Start()
    {
        if (!HasAuthority) return;

        ChooseBearing();
        SitOnGround();
    }

    /// <summary>
    /// Turn the site to a random bearing, rejecting any that would bury the door. The map is
    /// forest nearly all the way round, so a rejection means that one direction is crowded
    /// rather than that the rules are wrong -- hence taking the last try rather than failing.
    /// </summary>
    private void ChooseBearing()
    {
        Physics.SyncTransforms();

        for (int i = 0; i < Mathf.Max(1, attempts); i++)
        {
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Physics.SyncTransforms();

            if (door == null) return;
            if (DoorSpotClear()) return;
        }

        Debug.LogWarning("Level2Site could not find a clear bearing for the door; using the last one.", this);
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
            Vector3 p = child.position;
            Vector3 found;

            if (GroundAt(p, out found))
            {
                child.position = new Vector3(p.x, found.y + child.localPosition.y, p.z);
                continue;
            }

            bool placed = false;
            for (int i = 0; i < 8 && !placed; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 side = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * nudgeRadius;

                if (GroundAt(side, out found))
                {
                    child.position = new Vector3(side.x, found.y + child.localPosition.y, side.z);
                    placed = true;
                    moved++;
                }
            }

            if (!placed) lost++;
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
