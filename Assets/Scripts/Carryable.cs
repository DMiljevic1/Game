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

    public CarryLoad(float moveMultiplier, float sprintMultiplier, bool allowSprint)
    {
        this.moveMultiplier = moveMultiplier;
        this.sprintMultiplier = sprintMultiplier;
        this.allowSprint = allowSprint;
    }

    /// <summary>Empty hands: no penalty. This is what a drop, a sale or a death resets to.</summary>
    public static CarryLoad None { get { return new CarryLoad(1f, 1f, true); } }
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
    [Tooltip("Walking speed while this is held, as a fraction of normal. 1 = no penalty.")]
    [Range(0.2f, 1f)] public float carryMoveMultiplier = 1f;

    [Tooltip("Sprint speed while this is held, as a fraction of normal sprint.")]
    [Range(0.2f, 1f)] public float carrySprintMultiplier = 1f;

    [Tooltip("Clear it for something too big to run with.")]
    public bool allowSprintWhileCarried = true;

    [Header("Held pose")]
    [Tooltip("Local offset and rotation while held, relative to the carry socket.")]
    public Vector3 heldPosition = Vector3.zero;
    public Vector3 heldEuler = Vector3.zero;

    private Collider[] colliders;
    private Rigidbody body;
    private bool held;

    public bool IsHeld { get { return held; } }

    /// <summary>How much this slows whoever holds it. Handed to PlayerMovement once on
    /// pick-up by the interactor -- nothing reads it per frame.</summary>
    public CarryLoad Load
    {
        get { return new CarryLoad(carryMoveMultiplier, carrySprintMultiplier, allowSprintWhileCarried); }
    }

    /// <summary>True if holding this costs the player anything at all. Used for prompts.</summary>
    public bool IsHeavy
    {
        get { return carryMoveMultiplier < 1f || carrySprintMultiplier < 1f || !allowSprintWhileCarried; }
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

    public virtual void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (held) return;
        options.Add(new InteractionOption(pickUpKey, "Pick up " + itemName));
    }

    public virtual void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (held || key != pickUpKey) return;
        interactor.Carry(this);
    }

    public virtual void OnPickedUp(Transform socket)
    {
        held = true;

        // Colliders off while held, or the item shoves the player around and blocks
        // the interaction ray.
        SetCollidersEnabled(false);
        if (body != null) body.isKinematic = true;

        transform.SetParent(socket, false);
        transform.localPosition = heldPosition;
        transform.localRotation = Quaternion.Euler(heldEuler);
    }

    public virtual void OnDropped(Vector3 position, Quaternion rotation)
    {
        held = false;

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
