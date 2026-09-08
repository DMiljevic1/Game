using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's single point of contact with the world: looks down the camera,
/// picks the one thing in reach, lists what can be done to it, and routes the key.
///
/// Key bindings live on the interactables (a door says E, a pickup says F), so a new
/// object cannot silently steal a key that something else already uses.
///
/// Co-op note: this is per-player and local. It only ever asks an interactable to
/// act; the interactable owns whatever shared state changes as a result.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Reach")]
    public Camera viewCamera;

    [Tooltip("How far the player can reach, in metres.")]
    public float range = 3.5f;

    [Tooltip("How far off centre an object may sit and still be picked, in degrees. " +
             "Bigger is more forgiving; anything at or past 90 would include things beside the player.")]
    [Range(1f, 85f)] public float aimTolerance = 32f;

    [Tooltip("How much being centred in view counts when two things compete.")]
    public float aimWeight = 1f;

    [Tooltip("How much being close counts when two things compete. Raise it to favour " +
             "the nearer object, lower it to favour whatever is straight ahead.")]
    public float distanceWeight = 0.5f;

    [Tooltip("What can be interacted with and what blocks line of sight.")]
    public LayerMask interactionMask = ~0;

    [Header("Keys")]
    [Tooltip("Drops whatever is being carried. Every other key is declared by the interactable itself.")]
    public KeyCode dropKey = KeyCode.Q;

    [Header("Carrying")]
    [Tooltip("Where a carried item is parented. Usually a child of the camera.")]
    public Transform carrySocket;

    [Tooltip("The vitals whose death drops the carried item. Found on this object if left empty.")]
    public PlayerVitals vitals;

    [Tooltip("Movement that a heavy item slows down. Found on this object if left empty.")]
    public PlayerMovement movement;

    [Tooltip("Optional four-slot pack. With one, picking up with full hands stows what you " +
             "are holding; without one, it drops it, which is the behaviour that came first.")]
    public PlayerInventory inventory;

    [Tooltip("How far above the ground a dropped item is placed, so it never ends up sunk into the floor.")]
    public float dropClearance = 0.05f;

    [Header("Noise")]
    [Tooltip("How far the sound of handling something carries. One rule for every " +
             "interactable, so a new one is audible the day it is written.")]
    public float interactNoiseRadius = 12f;

    private readonly List<InteractionOption> options = new List<InteractionOption>();
    private readonly List<InteractionOption> heldOptions = new List<InteractionOption>();

    // Reused every frame: the target search runs in Update and must not allocate.
    // Sized well above what a 3.5 m sphere holds indoors (walls, floor, ceiling and
    // furniture together run to about thirty), because anything past the end of the
    // buffer is silently dropped - and the one dropped could be the interactable.
    private readonly Collider[] overlapBuffer = new Collider[64];

    // Reused by the drop grounding raycast, for the same no-garbage reason.
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private readonly List<Candidate> candidates = new List<Candidate>(16);
    private static readonly System.Comparison<Candidate> ByScore = CompareByScore;

    private static int CompareByScore(Candidate a, Candidate b)
    {
        return a.score.CompareTo(b.score);
    }

    private Carryable carried;
    private IInteractable target;

    public Carryable Carried { get { return carried; } }
    public bool IsCarrying { get { return carried != null; } }

    /// <summary>What the player is currently aiming at, or null. Read-only: real UI
    /// will want this, and it keeps the placeholder OnGUI from being the only reader.</summary>
    public IInteractable Target { get { return target; } }

    /// <summary>The actions available on <see cref="Target"/> this frame.</summary>
    public IList<InteractionOption> Options { get { return options; } }

    /// <summary>The carried item as T, or null. Lets interactables ask "holding a fuel can?".</summary>
    public T GetCarried<T>() where T : Carryable
    {
        return carried as T;
    }

    void OnEnable()
    {
        // Dying is the one thing that takes an item out of your hands without you
        // asking. The interactor owns the carried reference, so the drop belongs here
        // rather than in PlayerVitals, which knows nothing about carrying.
        if (vitals == null) vitals = GetComponent<PlayerVitals>();
        if (vitals != null) vitals.OnDied += DropCarriedOnDeath;
    }

    void OnDisable()
    {
        if (vitals != null) vitals.OnDied -= DropCarriedOnDeath;
    }

    void Start()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (movement == null)
        {
            Debug.LogError("PlayerInteractor on " + name + " has no PlayerMovement; heavy items will not slow the player.", this);
        }
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera == null)
        {
            Debug.LogError("PlayerInteractor on " + name + " has no camera; interaction will not work.", this);
        }
        if (carrySocket == null)
        {
            Debug.LogError("PlayerInteractor on " + name + " has no carry socket; items cannot be held.", this);
        }
    }

    void Update()
    {
        AcquireTarget();

        KeyCode consumed = KeyCode.None;

        if (target != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (Input.GetKeyDown(options[i].key))
                {
                    consumed = options[i].key;
                    target.Interact(this, options[i].key);

                    // Handling anything is audible. Emitted here, once, for every
                    // interactable there will ever be -- so nothing can forget to.
                    // Keys routed to a HELD item are deliberately silent: flicking a
                    // torch switch must not give you away.
                    Noise.Emit(transform.position, interactNoiseRadius, gameObject);
                    break;
                }
            }
        }

        // What is in the hands gets keys too: you switch a torch on without looking
        // at it. Its colliders are off while held, so it can never be the aimed
        // target as well - and if it shares a key with what you are aiming at, the
        // target wins, so one press can never fire two actions.
        AcquireHeldOptions();
        for (int i = 0; i < heldOptions.Count; i++)
        {
            KeyCode key = heldOptions[i].key;
            if (key == consumed || key == dropKey) continue;

            if (Input.GetKeyDown(key))
            {
                carried.Interact(this, key);
                break;
            }
        }

        if (carried != null && Input.GetKeyDown(dropKey))
        {
            DropCarried();
        }
    }

    /// <summary>What the carried item can do right now, if anything.</summary>
    private void AcquireHeldOptions()
    {
        heldOptions.Clear();
        if (carried != null) carried.GetOptions(this, heldOptions);
    }

    /// <summary>
    /// Pick the one thing the player means. A single centre ray is not enough: it
    /// demands pixel-perfect aim, and it gives up entirely the moment it lands on
    /// something inert, so a doorframe an inch from the handle hides the door.
    ///
    /// So: a dead-centre hit always wins, and otherwise everything within reach is
    /// scored on how centred and how close it is. Line of sight is checked per
    /// candidate, which is what keeps this from reaching through walls.
    /// </summary>
    public void AcquireTarget()
    {
        target = null;
        options.Clear();

        if (viewCamera == null) return;

        Transform eye = viewCamera.transform;
        Vector3 origin = eye.position;
        Vector3 forward = eye.forward;

        // Deliberate aim stays authoritative: if the player is pointing straight at
        // something usable, the forgiving search must never talk them out of it.
        RaycastHit centre;
        if (Physics.Raycast(origin, forward, out centre, range, interactionMask, QueryTriggerInteraction.Collide)
            && centre.transform.root != transform.root
            && Accept(centre.collider.GetComponentInParent<IInteractable>()))
        {
            return;
        }

        candidates.Clear();
        int count = Physics.OverlapSphereNonAlloc(origin, range, overlapBuffer, interactionMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;
            if (col.transform.root == transform.root) continue;   // ourselves, and anything in our hands

            IInteractable candidate = col.GetComponentInParent<IInteractable>();
            if (candidate == null) continue;
            if (carried != null && ReferenceEquals(candidate, carried)) continue;

            Vector3 aimPoint = AimPointOn(col, origin, forward);
            Vector3 toPoint = aimPoint - origin;
            float distance = toPoint.magnitude;
            if (distance > range) continue;

            // Angle also settles "behind the player" for free: with a tolerance
            // under 90 degrees, nothing beside or behind us can ever qualify.
            float angle = distance < 0.001f ? 0f : Vector3.Angle(forward, toPoint);
            if (angle > aimTolerance) continue;

            float score = (angle / aimTolerance) * aimWeight + (distance / range) * distanceWeight;

            // One object can contribute several colliders (a counter is three boxes).
            // Keep only its best-scoring face, so a big object cannot crowd the list.
            int existing = IndexOf(candidate);
            if (existing >= 0)
            {
                if (score < candidates[existing].score) candidates[existing] = new Candidate(candidate, col, aimPoint, score);
                continue;
            }
            candidates.Add(new Candidate(candidate, col, aimPoint, score));
        }

        candidates.Sort(ByScore);

        // Walk best-first and take the first one that is both visible and willing.
        // A near object that refuses (an empty-handed player at the sell counter)
        // must not hide the door behind it, and line of sight is only paid for on
        // candidates we would actually use.
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate c = candidates[i];
            if (!HasLineOfSight(origin, c.aimPoint, c.collider)) continue;
            if (Accept(c.interactable)) return;
        }
    }

    private int IndexOf(IInteractable interactable)
    {
        for (int i = 0; i < candidates.Count; i++)
            if (ReferenceEquals(candidates[i].interactable, interactable)) return i;
        return -1;
    }

    private struct Candidate
    {
        public readonly IInteractable interactable;
        public readonly Collider collider;
        public readonly Vector3 aimPoint;
        public readonly float score;

        public Candidate(IInteractable interactable, Collider collider, Vector3 aimPoint, float score)
        {
            this.interactable = interactable;
            this.collider = collider;
            this.aimPoint = aimPoint;
            this.score = score;
        }
    }

    /// <summary>Take this interactable as the target, if it actually offers anything.</summary>
    private bool Accept(IInteractable candidate)
    {
        options.Clear();
        if (candidate == null) return false;

        candidate.GetOptions(this, options);
        if (options.Count == 0) return false;   // it refused; leave the list empty

        target = candidate;
        return true;
    }

    /// <summary>
    /// The point on a collider nearest the view ray, measured at the object's own
    /// depth. This is what makes the tolerance scale with size: a door is forgiving
    /// across its whole face, while a dropped torch still gets a real area to aim
    /// at rather than a single point.
    /// </summary>
    private static Vector3 AimPointOn(Collider col, Vector3 origin, Vector3 forward)
    {
        Vector3 onRay = origin + forward * Mathf.Max(0f, Vector3.Dot(col.bounds.center - origin, forward));

        // ClosestPoint is not supported on a concave mesh collider, so fall back to
        // the bounding box for those rather than throwing.
        MeshCollider mesh = col as MeshCollider;
        if (mesh != null && !mesh.convex) return col.bounds.ClosestPoint(onRay);

        return col.ClosestPoint(onRay);
    }

    /// <summary>Is anything solid standing between the eye and the object?</summary>
    private bool HasLineOfSight(Vector3 origin, Vector3 point, Collider candidate)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance <= 0.05f) return true;   // all but touching it

        // Stop just short of the surface so the object never occludes itself, and
        // ignore triggers: a trigger volume is not a wall.
        RaycastHit blocker;
        if (!Physics.Raycast(origin, delta / distance, out blocker, distance - 0.02f,
                             interactionMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        if (blocker.transform.root == transform.root) return true;                  // our own body
        if (blocker.transform.IsChildOf(candidate.transform.root)) return true;     // another part of the same object
        return false;
    }

    /// <summary>
    /// Why a pickup would be refused right now, or "" if it would succeed. There are exactly
    /// two reasons, and they need different answers from the player -- one is solved by
    /// selling or dropping something, the other only by putting down what is in your hands --
    /// so the prompt says which.
    /// </summary>
    public string PickUpRefusalReason
    {
        get
        {
            if (carried == null) return "";
            if (inventory == null) return "";   // no pack: the old behaviour drops what you hold

            if (!carried.canBeStoredInInventory) return "put the " + carried.itemName + " down first";
            return inventory.CanStowCarried ? "" : "hands and pack full";
        }
    }

    /// <summary>
    /// Can the player take something new into their hands right now? False only when the
    /// hands are full AND what is in them cannot be put away -- the one case where a pickup
    /// has to be refused, because the alternative is trading away what you hold.
    /// </summary>
    public bool CanPickUp { get { return PickUpRefusalReason.Length == 0; } }

    /// <summary>
    /// Take an item into the hands. With full hands and a pack, what is already held is
    /// stowed; with full hands and no pack, it is dropped, as it always was. If the pack
    /// is full too the pickup is refused outright, so the item in your hands is never lost
    /// or swapped for the one on the floor.
    /// </summary>
    public void Carry(Carryable item)
    {
        if (item == null || carrySocket == null) return;

        if (carried != null)
        {
            if (inventory != null)
            {
                if (!inventory.StowCarried()) return;
            }
            else DropCarried();
        }

        carried = item;
        item.OnPickedUp(carrySocket);

        // The item's weight is applied here rather than read every frame, so movement
        // knows nothing about carrying and there is only ever one place it is set.
        ApplyCarryLoad();
    }

    /// <summary>
    /// Push whatever is in the hands (or nothing) at the movement script. Every route
    /// in and out of the hands ends here, so a penalty can never outlive the item that
    /// caused it -- drop, sell and death all clear it by passing through this.
    /// </summary>
    private void ApplyCarryLoad()
    {
        if (movement == null) return;

        if (carried != null) movement.SetCarryLoad(carried.Load);
        else movement.ClearCarryLoad();
    }

    /// <summary>Put the carried item down in front of the player.</summary>
    public void DropCarried()
    {
        DropCarriedAt(transform.position + transform.forward * 1.2f, transform.rotation);
    }

    /// <summary>
    /// Put the carried item down at a given spot, resting on whatever is below it.
    /// One drop path for every reason an item leaves the hands, so a death drop and a
    /// Q drop cannot end up behaving differently.
    /// </summary>
    public void DropCarriedAt(Vector3 where, Quaternion rotation)
    {
        if (carried == null) return;

        Carryable item = carried;
        carried = null;                       // cleared before the item is told, so nothing can re-enter
        ApplyCarryLoad();                     // hands empty: full speed back immediately

        item.OnDropped(GroundAt(where), rotation);
    }

    /// <summary>
    /// Death takes the item out of your hands and leaves it exactly where you fell,
    /// as an ordinary world pickup: it keeps its script, its value and its prompt, and
    /// anyone can collect it. The item is never destroyed and never sold for you.
    ///
    /// Co-op note: this runs wherever the death did, and it hands the item back to the
    /// world rather than to another player, so there is no per-player ownership to
    /// replicate later - the drop becomes a server-side call and the item's transform
    /// is the only thing a client needs to see.
    /// </summary>
    private void DropCarriedOnDeath()
    {
        if (carried == null) return;   // empty-handed death changes nothing

        // Use the recorded death position: Respawn is about to move the player.
        Vector3 where = vitals != null ? vitals.LastDeathPosition : transform.position;
        DropCarriedAt(where, transform.rotation);
    }

    /// <summary>
    /// Drops <paramref name="from"/> onto the ground under it, lifted by
    /// <see cref="dropClearance"/> so the item sits on the floor instead of inside it.
    /// Skips the player and anything else walking around, so a monster standing over
    /// the corpse cannot leave the loot hovering at chest height.
    /// </summary>
    private Vector3 GroundAt(Vector3 from)
    {
        Vector3 origin = from + Vector3.up * 1.0f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 4f, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearest = float.MaxValue;
        Vector3 place = from;

        for (int i = 0; i < count; i++)
        {
            Transform hitRoot = groundHits[i].transform.root;
            if (hitRoot == transform.root) continue;                                  // our own body
            if (hitRoot.GetComponent<CharacterController>() != null) continue;         // a monster standing in the way

            if (groundHits[i].distance < nearest)
            {
                nearest = groundHits[i].distance;
                place = groundHits[i].point;
                found = true;
            }
        }

        if (!found) place = from;
        return place + Vector3.up * dropClearance;
    }

    /// <summary>Remove the carried item from the hands without placing it (it was consumed).</summary>
    public void ConsumeCarried()
    {
        carried = null;
        ApplyCarryLoad();
    }

    void OnGUI()
    {
        // Crosshair, so the player knows the interaction is aim-based.
        Hud.Label(new Rect(0f, Screen.height * 0.5f - Hud.Prompt.fontSize * 0.5f, Screen.width, Hud.Prompt.fontSize),
                  "+", Hud.Centered, new Color(1f, 1f, 1f, 0.75f));

        // Every available action, one per line, each showing its own key.
        for (int i = 0; i < options.Count; i++)
        {
            float y = Screen.height * 0.5f + Hud.Prompt.fontSize * (0.9f + i * 1.3f);
            Hud.Label(new Rect(0f, y, Screen.width, Hud.Prompt.fontSize * 1.6f),
                      "[" + options[i].key + "]  " + options[i].label, Hud.Prompt);
        }

        if (carried != null)
        {
            // Whatever the held item can do is listed beside it, so its key is never
            // something the player has to already know.
            // Say when the item is what is slowing you down, or being slow reads as a bug.
            string weight = carried.IsHeavy
                ? (carried.allowSprintWhileCarried ? "  (heavy)" : "  (too heavy to run)")
                : "";

            string line = "Carrying: " + carried.itemName + weight + "   [" + dropKey + "] drop";
            for (int i = 0; i < heldOptions.Count; i++)
            {
                line += "   [" + heldOptions[i].key + "] " + heldOptions[i].label;
            }

            // Above the inventory bar's reserved strip, so the two never overlap.
            Hud.Label(new Rect(14f, Screen.height - Hud.BottomBarHeight - Hud.LineHeight - 6f, 900f, Hud.LineHeight),
                      line, Hud.Readout, new Color(1f, 0.92f, 0.6f));
        }
    }
}
