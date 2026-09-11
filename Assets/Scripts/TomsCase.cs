using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tom's iron-bound survey case, with the map inside: the thing Level 1 is about.
///
/// It is a plain Carryable with the Large load, so every existing rule applies for free --
/// slower than a chasing monster, no sprint, never in the pack (so no torch in hand while
/// you carry it), and dropped where you fall if you die. It is deliberately not a
/// Valuable, so the sell counter never offers to buy it.
///
/// The one thing it adds is noise: wrenching it free the first time is loud, so taking it
/// is the moment the night notices you. It is an ordinary Noise, heard or not by whatever
/// happens to be in range -- a lure, never a spawn and never a guaranteed attack.
/// </summary>
[DisallowMultipleComponent]
public class TomsCase : Carryable
{
    [Header("Taking it")]
    [Tooltip("How far the clatter of the first lift carries, in metres. Picking it up again " +
             "after a drop is no louder than handling anything else.")]
    public float liftNoiseRadius = 30f;

    private bool lifted;
    private bool opened;

    /// <summary>True once it has been taken from the camp.</summary>
    public bool HasBeenLifted { get { return lifted; } }

    /// <summary>True once it has been opened at home. It is scenery from then on.</summary>
    public bool IsOpened { get { return opened; } }

    protected override void Awake()
    {
        // Before base.Awake, as Valuable does, so the load is right if anything reads it early.
        ApplyLargeLoad();
        base.Awake();

        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "Tom's case";
    }

    // Keeps the Inspector honest, so the multipliers shown are the ones the player feels.
    void OnValidate()
    {
        ApplyLargeLoad();
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld || opened) return;
        options.Add(new InteractionOption(pickUpKey, "Pick up " + itemName + " - no sprint" + PickUpRefusal(interactor)));
    }

    public override void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (opened) return;
        base.Interact(interactor, key);
    }

    public override void OnPickedUp(Transform socket)
    {
        // From where it lay, before it moves into the hands.
        if (!lifted)
        {
            lifted = true;
            Noise.Emit(transform.position, liftNoiseRadius, gameObject);
        }

        base.OnPickedUp(socket);
    }

    /// <summary>Set down and opened. Only <see cref="CaseTable"/> calls this.</summary>
    public void OpenAt(Vector3 position, Quaternion rotation)
    {
        OnDropped(position, rotation);
        opened = true;
    }
}
