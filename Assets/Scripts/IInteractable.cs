/// <summary>
/// Anything the player can look at and press the interact key on.
/// PlayerInteractor picks exactly one target per frame, so interactables never
/// have to do their own proximity checks or fight each other over the key.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Text to show while this is the target, e.g. "Press E to open".
    /// Return null or empty to show nothing and refuse interaction.
    /// </summary>
    string GetPrompt(PlayerInteractor interactor);

    void Interact(PlayerInteractor interactor);
}
