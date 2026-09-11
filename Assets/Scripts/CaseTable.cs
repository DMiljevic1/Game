using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The kitchen table, where Tom's case is opened and Level 1 ends.
///
/// An ordinary IInteractable, in the same shape as the generator's refuel and the sell
/// counter: it offers nothing at all unless you are holding the case, so it is invisible
/// until the moment it matters.
/// </summary>
[DisallowMultipleComponent]
public class CaseTable : MonoBehaviour, IInteractable
{
    [Tooltip("Opens the case. Declared here, so nothing can silently steal it.")]
    public KeyCode openKey = KeyCode.E;

    [Tooltip("Where the case is set down when opened. The table's own transform if left empty.")]
    public Transform restPoint;

    [Tooltip("The level objective. Falls back to Expedition.Instance if left empty.")]
    public Expedition expedition;

    void Start()
    {
        if (expedition == null) expedition = Expedition.Instance;
        if (expedition == null)
        {
            Debug.LogError("CaseTable on " + name + " has no Expedition; the level can never be finished.", this);
        }
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("CaseTable on " + name + " has no collider, so it can never be looked at.", this);
        }
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (expedition == null || expedition.IsComplete) return;

        TomsCase held = interactor.GetCarried<TomsCase>();
        if (held == null) return;   // nothing to open: offer nothing

        options.Add(new InteractionOption(openKey, "Open " + held.itemName));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != openKey || expedition == null) return;

        TomsCase held = interactor.GetCarried<TomsCase>();
        if (held == null) return;

        // Out of the hands without being dropped, then laid on the table.
        interactor.ConsumeCarried();

        Transform at = restPoint != null ? restPoint : transform;
        held.OpenAt(at.position, at.rotation);

        expedition.CompleteLevel();
    }
}
