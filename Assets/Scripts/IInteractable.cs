using System.Collections.Generic;
using UnityEngine;

/// <summary>One available action on an interactable: which key, and what to call it.</summary>
public struct InteractionOption
{
    public KeyCode key;
    public string label;

    public InteractionOption(KeyCode key, string label)
    {
        this.key = key;
        this.label = label;
    }
}

/// <summary>
/// Anything the player can look at and act on. An interactable may offer several
/// actions at once (the generator offers refuel and start together), so it fills a
/// list rather than returning one prompt.
///
/// PlayerInteractor picks exactly one target per frame, so interactables never do
/// their own proximity checks or read the keyboard themselves.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Add every action currently available to <paramref name="options"/>.
    /// Add nothing to refuse interaction entirely. The list is cleared for you.
    /// </summary>
    void GetOptions(PlayerInteractor interactor, List<InteractionOption> options);

    /// <summary>Run the action bound to <paramref name="key"/>.</summary>
    void Interact(PlayerInteractor interactor, KeyCode key);
}
