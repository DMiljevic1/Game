using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The way out of Level 1, and the only thing that ends it.
///
/// It lives in three states and never goes backwards:
///
///   1. **Hidden.** Renderers off and colliders off, so there is not even an invisible wall
///      to walk into. Nothing at this spot reads as a door; the only thing here is the
///      <see cref="RevealCircle"/> painted on the ground, and that is invisible too unless a
///      <see cref="UVFlashlight"/> is on it.
///   2. **Revealed.** The circle has been dusted with <see cref="MagicPowder"/>. The door is
///      solid and plainly visible from now on, in any light, and it is locked.
///   3. **Open.** A <see cref="CompleteKey"/> has turned in it. It swings, and
///      <see cref="Expedition.CompleteLevel"/> fires.
///
/// It offers the unlock only while the key is actually in your hands -- the sell-counter
/// rule -- so an ordinary IInteractable is all it needs to be.
///
/// Co-op note: both state changes are authority-owned, the same shape as a sale. Clients see
/// the renderers and the swing.
/// </summary>
[DisallowMultipleComponent]
public class Level2Door : MonoBehaviour, IInteractable
{
    [Tooltip("Turns the key in the lock. Declared here, so nothing can silently steal it.")]
    public KeyCode unlockKey = KeyCode.E;

    [Tooltip("Everything that must not exist until the powder is scattered. Found in this " +
             "object's children if left empty.")]
    public Renderer[] hidden;

    [Tooltip("Colliders switched on with them, so nothing bumps into a door that is not there yet.")]
    public Collider[] solids;

    [Tooltip("The hinge that swings. Rotated like a door hinge; its child is the leaf.")]
    public Transform hinge;

    public float openAngle = 96f;
    public float openSpeed = 42f;

    [Tooltip("The level objective. Falls back to Expedition.Instance if left empty.")]
    public Expedition expedition;

    private bool revealed;
    private bool unlocked;
    private float swung;
    private Quaternion shut;

    /// <summary>True once the powder has made it solid.</summary>
    public bool IsRevealed { get { return revealed; } }

    /// <summary>True once the key has turned.</summary>
    public bool IsUnlocked { get { return unlocked; } }

    // Authority seam, as with Wallet and Store.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        if (hidden == null || hidden.Length == 0) hidden = GetComponentsInChildren<Renderer>(true);
        if (solids == null || solids.Length == 0) solids = GetComponentsInChildren<Collider>(true);
        if (hinge != null) shut = hinge.localRotation;

        Apply(false);
    }

    void Start()
    {
        if (expedition == null) expedition = Expedition.Instance;
        if (expedition == null)
        {
            Debug.LogError("Level2Door on " + name + " has no Expedition; the level can never be finished.", this);
        }
    }

    void Update()
    {
        if (hinge == null || !unlocked || swung >= openAngle) return;

        // A plain float eased toward the target, like DoorInteraction -- no coroutine, so
        // nothing is left running and the swing cannot be started twice.
        swung = Mathf.MoveTowards(swung, openAngle, openSpeed * Time.deltaTime);
        hinge.localRotation = shut * Quaternion.Euler(0f, swung, 0f);
    }

    /// <summary>
    /// The powder has been scattered. Only <see cref="RevealCircle"/> calls this.
    /// One way: a door that has been found stays found.
    /// </summary>
    public void Reveal()
    {
        if (revealed || !HasAuthority) return;

        revealed = true;
        Apply(true);
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (!revealed || unlocked) return;   // nothing to offer a door that is not there, or is open

        // The key has to be in your hands, not in your pack: the last act of the level is
        // something you do deliberately, the same way selling is.
        if (interactor.GetCarried<CompleteKey>() == null) return;

        options.Add(new InteractionOption(unlockKey, "Unlock the door with the finished key"));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != unlockKey || !revealed || unlocked || !HasAuthority) return;

        CompleteKey held = interactor.GetCarried<CompleteKey>();
        if (held == null) return;

        // Consumed the way a sale consumes a valuable: out of the hands, then gone.
        interactor.ConsumeCarried();
        Destroy(held.gameObject);

        unlocked = true;
        if (expedition != null) expedition.CompleteLevel();
    }

    private void Apply(bool on)
    {
        if (hidden != null)
            for (int i = 0; i < hidden.Length; i++)
                if (hidden[i] != null) hidden[i].enabled = on;

        if (solids != null)
            for (int i = 0; i < solids.Length; i++)
                if (solids[i] != null) solids[i].enabled = on;
    }
}
