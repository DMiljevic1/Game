using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A jar of powder that makes hidden things solid.
///
/// **It is a probe, not a one-shot.** You pour a little where you are looking and watch what
/// the dust does: it settles flat on ordinary ground, and clings to whatever was standing
/// there invisibly. That is the whole mechanic -- the player's question stops being "where
/// exactly is the door" and becomes "is it worth a pinch of powder to find out?", which is a
/// question they can ask over and over while they sweep a hillside.
///
/// **The jar never runs out.** It is bought once and kept: a permanent tool, not a
/// consumable. That is deliberate -- the powder's whole job is to be asked the same question
/// over and over while the player sweeps a hillside, and anything that made each pour cost
/// something would push them back towards standing still and guessing.
///
/// It owns no knowledge of what it might find. It pours, looks for anything nearby that
/// wants to be revealed, and tells it -- so the next hidden thing this game invents only has
/// to offer itself up the same way, and this item needs no change at all.
/// </summary>
[DisallowMultipleComponent]
public class MagicPowder : Carryable
{
    [Header("Pouring")]
    [Tooltip("Pours a little where you are looking. Declared here, so nothing can silently " +
             "steal it -- and it is routed by PlayerInteractor like every other key, never " +
             "read with Input directly.")]
    public KeyCode pourKey = KeyCode.Mouse0;

    [Tooltip("How far in front of you the powder can be poured, in metres.")]
    public float pourRange = 4.5f;

    [Tooltip("How far from the dust a hidden thing is still touched by it. Generous, because " +
             "hunting for a two-metre circle with a two-metre probe would be miserable.")]
    public float revealRadius = 3.5f;

    [Tooltip("How far the sound of scattering it carries. A world noise, like the store's " +
             "purchase, so crouching does not quieten it.")]
    public float pourNoiseRadius = 6f;

    [Header("What it leaves")]
    [Tooltip("The dust material. Falls back to whatever this item's own powder is made of.")]
    public Material dustMaterial;

    [Tooltip("How wide a poured patch is, in metres.")]
    public float patchSize = 1.3f;

    [Header("Pouring motion")]
    [Tooltip("How long the whole tip-and-return takes. Short: it has to feel like a flick of " +
             "the wrist, not a wind-up.")]
    public float pourSeconds = 0.42f;

    [Tooltip("Where in that motion the powder actually leaves the jar. A little in, so the " +
             "dust appears BECAUSE the jar tipped rather than a moment before it.")]
    public float pourDelay = 0.13f;

    [Tooltip("How far the jar is tipped at the peak of the motion, in degrees about its own " +
             "right-hand axis. It has to go PAST 90: the jar's mouth is its local +Y, so at 78 " +
             "degrees the mouth still points forward and the jar reads as being presented " +
             "rather than poured. At 115 the mouth is down and away, over the ground aimed at.")]
    public float pourTilt = 115f;

    [Tooltip("How far the jar dips and pushes forward at the peak, in metres.")]
    public Vector3 pourNudge = new Vector3(0.02f, -0.06f, 0.07f);

    private readonly Collider[] nearby = new Collider[24];

    // The motion, and the pour it is going to deliver partway through.
    private float pourTime = -1f;
    private bool pourPending;
    private Vector3 pourPoint;

    /// <summary>True while the jar is mid-tip. Read by nothing yet; here for feel-tuning.</summary>
    public bool IsPouring { get { return pourTime >= 0f; } }

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "magic powder";

        if (dustMaterial == null)
        {
            // Whatever the jar's own powder is made of, so the patch always matches the item.
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.name == "Powder") { dustMaterial = r.sharedMaterial; break; }
        }
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (!IsHeld)
        {
            options.Add(new InteractionOption(pickUpKey, "Take the " + itemName + PickUpRefusal(interactor)));
            return;
        }

        // The store is the one place in the game the mouse means something else. Offering the
        // pour there would scatter powder every time the player clicked a Buy button.
        if (Store.IsAnyOpen) return;

        options.Add(new InteractionOption(pourKey, "Pour a little powder"));
    }

    // Every way the jar can leave the hands cancels the motion. A stowed object's Update does
    // not run, so without these a jar put away mid-tip would resume the pour when taken out.
    public override void OnPickedUp(Transform socket) { pourTime = -1f; pourPending = false; base.OnPickedUp(socket); }
    public override void OnStowed() { pourTime = -1f; pourPending = false; base.OnStowed(); }
    public override void OnDropped(Vector3 position, Quaternion rotation) { pourTime = -1f; pourPending = false; base.OnDropped(position, rotation); }

    public override void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (IsHeld && key == pourKey)
        {
            Pour(interactor);
            return;
        }
        base.Interact(interactor, key);
    }

    /// <summary>
    /// Scatter one pour at whatever the player is looking at, and tell anything hidden nearby
    /// that it has been found.
    /// </summary>
    private void Pour(PlayerInteractor interactor)
    {
        // Mid-tip, the jar is already pouring. This is the motion finishing, not a limit on
        // how often the powder may be used -- there is no such limit.
        if (IsPouring) return;

        Vector3 where;
        if (!AimedGround(interactor, out where)) return;

        // The spot is pinned at the click, so the dust lands where the player was looking
        // even if they have turned away by the time the jar finishes tipping.
        pourPoint = where;
        pourPending = true;
        pourTime = 0f;
    }

    /// <summary>
    /// Tip the jar over and bring it back. A single float shaped into an out-and-back curve,
    /// like <see cref="DamageFeedback"/>'s flash -- no coroutine, so a second click can only
    /// ever be ignored rather than leaving two motions fighting over the same transform.
    /// </summary>
    void Update()
    {
        if (pourTime < 0f) return;

        // Leaving the hands mid-pour cancels the whole thing: the pose belongs to whoever
        // picks it up next, and a pour that lands after the jar is on the floor is a lie.
        if (!IsHeld || IsStowed) { EndPour(); return; }

        pourTime += Time.deltaTime;

        if (pourPending && pourTime >= pourDelay)
        {
            pourPending = false;
            Deliver(pourPoint);
        }

        float span = Mathf.Max(0.01f, pourSeconds);
        if (pourTime >= span) { EndPour(); return; }

        // 0 -> 1 -> 0 over the motion, so the jar tips and rights itself in one gesture.
        float k = Mathf.Sin(Mathf.Clamp01(pourTime / span) * Mathf.PI);

        // Relative to the authored held pose, never absolute -- the carry socket is
        // deliberately cocked, and overwriting the pose outright would fight that.
        transform.localRotation = Quaternion.Euler(heldEuler) * Quaternion.Euler(pourTilt * k, 0f, 0f);
        transform.localPosition = heldPosition + pourNudge * k;
    }

    private void EndPour()
    {
        pourTime = -1f;
        pourPending = false;

        if (IsHeld && !IsStowed)
        {
            transform.localRotation = Quaternion.Euler(heldEuler);
            transform.localPosition = heldPosition;
        }
    }

    /// <summary>What the pour actually does, once the jar has tipped far enough to do it.</summary>
    private void Deliver(Vector3 where)
    {
        // Keys routed to a held item are silent by rule -- that is for switches, not for
        // tipping a jar out on the ground. Straight to the bus, like the store's purchase.
        Noise.Emit(where, pourNoiseRadius, gameObject);

        PowderPatch patch = Scatter(where);
        bool found = Reveal(where);

        if (patch != null)
        {
            patch.Report(found ? "The dust catches on something that is not there."
                               : "The dust settles. Nothing here.", found);
        }
    }

    /// <summary>The ground the player is looking at, or false if they are aiming at the sky.</summary>
    private bool AimedGround(PlayerInteractor interactor, out Vector3 point)
    {
        point = transform.position;

        Camera eye = interactor.viewCamera;
        if (eye == null) return false;

        RaycastHit hit;
        if (Physics.Raycast(eye.transform.position, eye.transform.forward, out hit, pourRange,
                            ~0, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        // Looking into the middle distance still pours at your feet rather than doing nothing,
        // so a pour is never silently swallowed.
        Vector3 ahead = interactor.transform.position + interactor.transform.forward * 1.2f;
        if (Physics.Raycast(ahead + Vector3.up * 2f, Vector3.down, out hit, 6f, ~0,
                            QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }
        return false;
    }

    /// <summary>Leave the dust where it fell. Purely visual; it never blocks or reveals anything.</summary>
    private PowderPatch Scatter(Vector3 where)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "PowderPatch";
        go.transform.position = where + Vector3.up * 0.012f;
        go.transform.localScale = new Vector3(patchSize * Random.Range(0.85f, 1.15f), 0.006f,
                                              patchSize * Random.Range(0.85f, 1.15f));
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Destroy(go.GetComponent<Collider>());   // dust is not something to walk into or aim at

        Renderer r = go.GetComponent<Renderer>();
        if (r != null && dustMaterial != null) r.sharedMaterial = dustMaterial;

        return go.AddComponent<PowderPatch>();
    }

    /// <summary>Tell anything hidden within reach that the powder has found it.</summary>
    private bool Reveal(Vector3 where)
    {
        bool found = false;

        int count = Physics.OverlapSphereNonAlloc(where, revealRadius, nearby, ~0,
                                                  QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (nearby[i] == null) continue;

            RevealCircle circle = nearby[i].GetComponentInParent<RevealCircle>();
            if (circle == null || circle.HasBeenUsed) continue;

            circle.Reveal();
            found = true;
        }
        return found;
    }
}
