using UnityEngine;

/// <summary>
/// Something that is standing right in front of you and does not exist yet.
///
/// <see cref="UVRevealed"/> hides a mark by switching its renderers off, which works because
/// a footprint on open ground leaves nothing behind when it goes. A door set into a rock face
/// cannot be hidden that way: switch its renderers off and you are left looking down a
/// corridor that carries on into the dark, which gives the secret away to anyone who walks up
/// to it. Switch its colliders off instead and you can walk straight through the wall.
///
/// So this hides by **swapping**: while concealed the <see cref="disguise"/> is what stands
/// here -- a plain slab of the same rock as everything around it -- and the thing itself is
/// not rendered at all. The powder swaps them over, once, and never back.
///
/// It is a pure state switch and holds no idea what it is concealing: whatever is on
/// <see cref="dormant"/> comes to life when the swap happens, so the door's own
/// <see cref="DoorInteraction"/> is an ordinary door that simply was not there before.
///
/// Co-op note: authority-owned, like a sale -- the swap is shared state. Clients see the
/// renderers change.
/// </summary>
[DisallowMultipleComponent]
public class Concealed : MonoBehaviour, IPowderRevealable
{
    [Header("What is really here")]
    [Tooltip("Renderers that do not exist until the powder finds them.")]
    public Renderer[] hidden;

    [Tooltip("Colliders switched on with them. The disguise is what has to keep the way shut " +
             "while this is hidden -- so make sure it does, rather than leaving these live to " +
             "do it, or there is an interactable with a live collider inside a wall the player " +
             "is not supposed to know about.")]
    public Collider[] solids;

    [Tooltip("Scripts that only start once it has been found -- the door's own " +
             "DoorInteraction, so there is no prompt on a wall.")]
    public Behaviour[] dormant;

    [Header("What stands here instead")]
    [Tooltip("The lie: a plain slab dressed as whatever surrounds it. Shown until the powder " +
             "lands, then gone. Its colliders go with it, so the way through opens up.")]
    public Renderer[] disguise;

    [Tooltip("Colliders belonging to the disguise, switched off with it.")]
    public Collider[] disguiseSolids;

    [Header("Being found")]
    [Tooltip("The always-live trigger the powder's overlap search hits. Never switched off, " +
             "or the thing could not be found at all. Excluded from the lists above.")]
    public Collider probeVolume;

    [Tooltip("How far the rock grinding back carries, in metres. A world noise, like the " +
             "powder's own: something heavy just moved.")]
    public float revealNoiseRadius = 14f;

    private bool revealed;

    public bool HasBeenRevealed { get { return revealed; } }

    // Authority seam, as with Level2Door and Wallet.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        if (probeVolume == null)
        {
            Debug.LogError("Concealed on " + name + " has no probe volume, so the powder can " +
                           "never find it.", this);
        }
        Apply(false);
    }

    public void Reveal()
    {
        if (revealed || !HasAuthority) return;

        revealed = true;
        Apply(true);

        Noise.Emit(transform.position, revealNoiseRadius, gameObject);
    }

    private void Apply(bool found)
    {
        Show(hidden, found);
        Enable(solids, found);

        Show(disguise, !found);
        Enable(disguiseSolids, !found);

        if (dormant != null)
        {
            for (int i = 0; i < dormant.Length; i++)
                if (dormant[i] != null) dormant[i].enabled = found;
        }
    }

    private static void Show(Renderer[] set, bool on)
    {
        if (set == null) return;
        for (int i = 0; i < set.Length; i++)
            if (set[i] != null) set[i].enabled = on;
    }

    private static void Enable(Collider[] set, bool on)
    {
        if (set == null) return;
        for (int i = 0; i < set.Length; i++)
            if (set[i] != null) set[i].enabled = on;
    }
}
