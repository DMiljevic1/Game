using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a dead player leaves behind: a body lying where they fell, belonging to them.
///
/// It is a separate object from the player on purpose. The player's own GameObject is
/// free to become a spectator once there is someone else to watch, and the body is an
/// ordinary <see cref="Carryable"/> -- heavy, hands-only, too big for the pack -- so
/// bringing a teammate home is the same pick-up and put-down as the television, and a
/// future teleporter only has to move a transform.
///
/// It owns the *interaction* (the prompts and the key) and nothing else. Whether a revive
/// is allowed, and what it costs, is decided by <see cref="Revival"/>.
///
/// Co-op note: <see cref="Owner"/> becomes a player id. The body is a server-spawned
/// world object; carrying it replicates exactly like carrying anything else.
/// </summary>
[DisallowMultipleComponent]
public class PlayerBody : Carryable
{
    [Header("Revive")]
    [Tooltip("Uses the Adrenaline in your hands on this body. Declared here, so nothing can silently steal it.")]
    public KeyCode reviveKey = KeyCode.E;

    private PlayerVitals owner;

    /// <summary>Whose body this is. Null only if that player has left the game.</summary>
    public PlayerVitals Owner { get { return owner; } }

    /// <summary>Tie the body to the player it belongs to. Called once, by Revival, as it is left.</summary>
    public void Bind(PlayerVitals player)
    {
        owner = player;
        itemName = player != null ? player.name + "'s body" : "body";
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld) return;   // in someone's hands the only thing to do with it is put it down

        Revival revival = Revival.Instance;
        string refusal = revival != null ? revival.ReviveRefusal(this, interactor) : "nothing can revive it";

        if (refusal.Length == 0)
        {
            options.Add(new InteractionOption(reviveKey, "Revive with adrenaline"));
        }

        // The pickup always says why there is no revive yet, so the player is never left
        // guessing what the body wants from them.
        string note = refusal.Length == 0 ? "" : "  (" + refusal + ")";
        options.Add(new InteractionOption(pickUpKey, "Pick up " + itemName + note + PickUpRefusal(interactor)));
    }

    public override void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (!IsHeld && key == reviveKey)
        {
            Revival revival = Revival.Instance;
            if (revival != null) revival.TryRevive(this, interactor);
            return;
        }

        base.Interact(interactor, key);
    }
}
