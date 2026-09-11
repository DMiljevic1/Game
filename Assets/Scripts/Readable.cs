using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something with words on it: a notice, a drawing, a scrap of paper. Press E and it is
/// shown on screen; the words live here, the panel is the reader's ReadableHud.
///
/// It holds no state at all -- which note a player has open is that player's own view,
/// so two people can read the same notice at once and nothing needs replicating.
/// </summary>
[DisallowMultipleComponent]
public class Readable : MonoBehaviour, IInteractable
{
    [Tooltip("Opens and closes it. Declared here, so nothing can silently steal it.")]
    public KeyCode readKey = KeyCode.E;

    [Tooltip("The action on the prompt, e.g. \"Read the notice\".")]
    public string prompt = "Read";

    [Tooltip("Heading on the panel. Leave empty for none.")]
    public string title = "";

    [TextArea(4, 12)]
    public string text = "";

    void Start()
    {
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("Readable on " + name + " has no collider, so it can never be looked at.", this);
        }
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        options.Add(new InteractionOption(readKey, prompt));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != readKey) return;

        // Looked up per press, never per frame.
        ReadableHud view = interactor.GetComponent<ReadableHud>();
        if (view == null)
        {
            Debug.LogError("Player " + interactor.name + " has no ReadableHud, so nothing can be read.", this);
            return;
        }
        view.Toggle(this);
    }
}
