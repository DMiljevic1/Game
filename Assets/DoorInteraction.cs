using System.Collections.Generic;
using UnityEngine;

// Put this on the door's HINGE pivot (an empty GameObject positioned at the
// door's edge, with the door mesh as its child) so rotation swings correctly.
// Targeting and the interact key are handled by PlayerInteractor.
public class DoorInteraction : MonoBehaviour, IInteractable
{
    public KeyCode useKey = KeyCode.E;
    public float openAngle = 90f; // always swings this direction (positive = one fixed side)
    public float openSpeed = 3f;
    public float restAngleThreshold = 2f; // degrees; below this the door counts as "settled"

    [Tooltip("Can a monster force this open? Clear it for a door that should stay shut.")]
    public bool canBeForced = true;

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private bool isOpen = false;
    private Collider[] doorColliders;

    void Start()
    {
        closedRotation = transform.localRotation;
        openRotation = Quaternion.Euler(0f, openAngle, 0f) * closedRotation;

        // Every collider in the group, not just the first: imported doors carry
        // separate colliders for the leaf, hinges and knobs.
        doorColliders = GetComponentsInChildren<Collider>();
        if (doorColliders.Length == 0)
        {
            Debug.LogError("DoorInteraction on " + name + " found no colliders; the door will never be solid.", this);
        }
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        options.Add(new InteractionOption(useKey, isOpen ? "Close door" : "Open door"));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != useKey) return;
        isOpen = !isOpen;
    }

    public bool IsOpen { get { return isOpen; } }

    /// <summary>Open or close from code — used by monsters forcing their way in.</summary>
    public void SetOpen(bool open)
    {
        isOpen = open;
    }

    void Update()
    {
        Quaternion target = isOpen ? openRotation : closedRotation;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * openSpeed);

        // Only solid when settled at closed or fully open; passable while actively swinging.
        bool settled = Quaternion.Angle(transform.localRotation, target) <= restAngleThreshold;
        for (int i = 0; i < doorColliders.Length; i++)
        {
            if (doorColliders[i] != null) doorColliders[i].enabled = settled;
        }
    }
}
