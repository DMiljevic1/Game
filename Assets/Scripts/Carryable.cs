using System.Collections.Generic;
using UnityEngine;

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

    [Tooltip("Local offset and rotation while held, relative to the carry socket.")]
    public Vector3 heldPosition = Vector3.zero;
    public Vector3 heldEuler = Vector3.zero;

    private Collider[] colliders;
    private Rigidbody body;
    private bool held;

    public bool IsHeld { get { return held; } }

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
