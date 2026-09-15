using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What holding something does to the person holding it. A plain value, so an item
/// describes its own weight and the movement script never has to know what kind of
/// thing is in the hands.
/// </summary>
public struct CarryLoad
{
    /// <summary>Walking speed while held, as a fraction of normal.</summary>
    public readonly float moveMultiplier;

    /// <summary>Sprint speed while held, as a fraction of normal sprint.</summary>
    public readonly float sprintMultiplier;

    /// <summary>False for something too bulky to run with.</summary>
    public readonly bool allowSprint;

    /// <summary>
    /// Hugged to the chest: walked at the carrier's own heavyWalkSpeed rather than a
    /// multiplier, with no sprint and no jump. The speed lives on PlayerMovement so every
    /// heavy thing is retuned in one Inspector field.
    /// </summary>
    public readonly bool heavy;

    public CarryLoad(float moveMultiplier, float sprintMultiplier, bool allowSprint, bool heavy)
    {
        this.moveMultiplier = moveMultiplier;
        this.sprintMultiplier = sprintMultiplier;
        this.allowSprint = allowSprint;
        this.heavy = heavy;
    }

    /// <summary>Empty hands: no penalty. This is what a drop, a sale or a death resets to.</summary>
    public static CarryLoad None { get { return new CarryLoad(1f, 1f, true, false); } }
}

/// <summary>
/// An item the player can pick up and carry. Subclass it to give the item a use
/// (see FuelCan); this base only handles being held and put down.
/// </summary>
[DisallowMultipleComponent]
public class Carryable : MonoBehaviour, IInteractable
{
    public string itemName = "Item";

    [Tooltip("Key used to pick this up.")]
    public KeyCode pickUpKey = KeyCode.F;

    [Header("Weight")]
    [Tooltip("Walking speed while this is held, as a fraction of normal. 1 = no penalty. " +
             "Ignored for a heavy load, which walks at PlayerMovement.heavyWalkSpeed.")]
    [Range(0.2f, 1f)] public float carryMoveMultiplier = 1f;

    [Tooltip("Sprint speed while this is held, as a fraction of normal sprint.")]
    [Range(0.2f, 1f)] public float carrySprintMultiplier = 1f;

    [Tooltip("Clear it for something too big to run with.")]
    public bool allowSprintWhileCarried = true;

    [Tooltip("Hugged to the chest: walked at PlayerMovement.heavyWalkSpeed, with no sprint and no jump. " +
             "Set by the Large preset; tick it by hand for anything else that heavy.")]
    public bool heavyLoad = false;

    [Tooltip("Can this go on your back? Clear it for anything too big to shoulder -- a " +
             "television is carried in your hands or not at all, so it can never take up " +
             "one of the four slots, and you cannot pick anything else up while holding it.")]
    public bool canBeStoredInInventory = true;

    [Header("Held pose")]
    [Tooltip("Local offset and rotation while held, relative to the carry socket.")]
    public Vector3 heldPosition = Vector3.zero;
    public Vector3 heldEuler = Vector3.zero;

    private Collider[] colliders;
    private Rigidbody body;
    private bool held;
    private bool stowed;

    /// <summary>
    /// True while this is in the player's possession rather than lying in the world --
    /// in the hands OR stowed in a pack slot. Every "is it still on the floor?" guard
    /// reads this, so stowing something cannot make it look pickable again.
    /// </summary>
    public bool IsHeld { get { return held; } }

    /// <summary>True while this is in a pack slot rather than in the hands.</summary>
    public bool IsStowed { get { return stowed; } }

    /// <summary>How much this slows whoever holds it. Handed to PlayerMovement once on
    /// pick-up by the interactor -- nothing reads it per frame.</summary>
    public CarryLoad Load
    {
        get { return new CarryLoad(carryMoveMultiplier, carrySprintMultiplier, allowSprintWhileCarried, heavyLoad); }
    }

    /// <summary>True if holding this costs the player anything at all. Used for prompts.</summary>
    public bool IsHeavy
    {
        get { return heavyLoad || carryMoveMultiplier < 1f || carrySprintMultiplier < 1f || !allowSprintWhileCarried; }
    }

    protected virtual void Awake()
    {
        colliders = GetComponentsInChildren<Collider>();
        body = GetComponent<Rigidbody>();

        if (colliders.Length == 0)
        {
            Debug.LogError("Carryable " + name + " has no collider, so it can never be looked at.", this);
        }
    }

    /// <summary>
    /// The "hugged to the chest" load: slower than a chasing monster, no running, no jumping,
    /// never in the pack. One copy, shared by Large loot and anything else that heavy (Tom's
    /// case). The speed itself is PlayerMovement.heavyWalkSpeed, so retuning it against the
    /// monster's chaseSpeed is one Inspector field on the Player and moves them all together.
    /// </summary>
    protected void ApplyLargeLoad()
    {
        heavyLoad = true;
        carryMoveMultiplier = 1f;        // unused: a heavy load walks at the carrier's heavyWalkSpeed
        carrySprintMultiplier = 1f;      // unused while sprinting is off
        allowSprintWhileCarried = false;

        // Too big to shoulder: a whole trip in your hands, never one of four things you
        // grabbed on the way -- and no flashlight in hand while you carry it.
        canBeStoredInInventory = false;
    }

    /// <summary>
    /// The note explaining why a pickup will be refused, or "". Lives here so every item
    /// says the same thing and a new Carryable gets the wording without knowing a pack
    /// exists -- being unable to pick something up with no explanation reads as a bug.
    /// </summary>
    protected static string PickUpRefusal(PlayerInteractor interactor)
    {
        if (interactor == null) return "";

        string reason = interactor.PickUpRefusalReason;
        return reason.Length == 0 ? "" : "  - " + reason;
    }

    public virtual void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (held) return;
        options.Add(new InteractionOption(pickUpKey, "Pick up " + itemName + PickUpRefusal(interactor)));
    }

    public virtual void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (held || key != pickUpKey) return;
        interactor.Carry(this);
    }

    public virtual void OnPickedUp(Transform socket)
    {
        held = true;
        stowed = false;

        // Colliders off while held, or the item shoves the player around and blocks
        // the interaction ray.
        SetCollidersEnabled(false);
        if (body != null) body.isKinematic = true;

        transform.SetParent(socket, false);
        transform.localPosition = heldPosition;
        transform.localRotation = Quaternion.Euler(heldEuler);
    }

    /// <summary>
    /// Into a pack slot: still the player's, just not in their hands. The object is
    /// deactivated -- never cloned, never destroyed -- so it comes back out as the same
    /// GameObject with the same script, value, name and state it had on the floor.
    ///
    /// <see cref="IsHeld"/> stays true because the item is still not in the world, so
    /// every existing "on the floor?" guard keeps meaning what it meant.
    ///
    /// Deactivating is also why a stowed flashlight goes dark and lights again when you take
    /// it out. Override if an item should keep running inside the pack.
    /// </summary>
    public virtual void OnStowed()
    {
        stowed = true;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Out of a slot. Only wakes the object up: the held pose is applied by
    /// <see cref="OnPickedUp"/> right after, so there is still exactly one place that
    /// decides how a held item sits.
    /// </summary>
    public virtual void OnUnstowed()
    {
        stowed = false;
        gameObject.SetActive(true);
    }

    public virtual void OnDropped(Vector3 position, Quaternion rotation)
    {
        held = false;

        // A slot is a place an item can leave from too, so undo the stow rather than
        // leaving an inactive object lying invisibly on the floor.
        stowed = false;
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        transform.SetParent(null, true);
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);

        SetCollidersEnabled(true);
        if (body != null) body.isKinematic = false;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = enabled;
        }
    }
}
