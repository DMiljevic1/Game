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

    [Header("Belongings")]
    [Tooltip("Tips everything the dead player was carrying onto the ground beside the body. " +
             "Declared here, so nothing can silently steal it.")]
    public KeyCode searchKey = KeyCode.R;

    [Tooltip("How far from the body its belongings are tipped out.")]
    public float spillRadius = 0.55f;

    // What the owner had on them when they died -- the hand item and every pack slot.
    // They are stowed, exactly as a pack slot stows them, so they are inactive, parented
    // here and travel with the body when someone shoulders it. Never cloned, never
    // destroyed: the same GameObjects that were in the player's hands and pack.
    private readonly List<Carryable> contents = new List<Carryable>();

    private PlayerVitals owner;

    /// <summary>Whose body this is. Null only if that player has left the game.</summary>
    public PlayerVitals Owner { get { return owner; } }

    /// <summary>Tie the body to the player it belongs to. Called once, by Revival, as it is left.</summary>
    public void Bind(PlayerVitals player)
    {
        owner = player;
        itemName = player != null ? player.name + "'s body" : "body";
    }

    /// <summary>How many of the owner's things are still on the body.</summary>
    public int ContentCount
    {
        get
        {
            for (int i = contents.Count - 1; i >= 0; i--) if (contents[i] == null) contents.RemoveAt(i);
            return contents.Count;
        }
    }

    /// <summary>
    /// Take everything the dead player owned. Called once, by <see cref="Revival"/>, as the
    /// body is left -- so the hand item and the four pack slots all end up here instead of
    /// in a heap on the floor, and carrying a teammate home brings their kit with them.
    /// </summary>
    public void TakeBelongings(PlayerInteractor from)
    {
        if (from == null) return;

        int first = contents.Count;
        from.ReleaseBelongings(contents);

        for (int i = first; i < contents.Count; i++)
        {
            Carryable item = contents[i];
            if (item == null) continue;

            // Parent it while it is still awake, then stow it: the same call a pack slot
            // makes, so the item keeps its script, value, name and state throughout.
            item.transform.SetParent(transform, false);
            item.transform.localPosition = Vector3.zero;
            item.OnStowed();
        }
    }

    /// <summary>
    /// Tip everything out onto the ground around the body. From there each piece is an
    /// ordinary world pickup again -- exactly what the old death drop left behind -- so
    /// nothing needs a second route into a player's hands. Also what a revive does with
    /// whatever is still on the body.
    /// </summary>
    public void Spill()
    {
        Revival revival = Revival.Instance;

        for (int i = 0; i < contents.Count; i++)
        {
            Carryable item = contents[i];
            if (item == null) continue;

            // A ring rather than one spot, so several pieces do not land inside each other.
            float angle = contents.Count <= 1 ? 0f : i * (Mathf.PI * 2f / contents.Count);
            Vector3 where = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spillRadius;
            if (revival != null) where = revival.GroundUnder(where, transform);

            item.OnDropped(where, transform.rotation);
        }

        contents.Clear();
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        Revival revival = Revival.Instance;

        // A revive already under way owns the body: it cannot be shouldered or emptied
        // mid-injection, and the only thing left to offer is calling it off.
        if (revival != null && revival.IsBeingRevived(this))
        {
            options.Add(new InteractionOption(reviveKey,
                string.Format("Reviving {0}...  {1:0.0}s   (cancel - the dose is spent either way)",
                              itemName, revival.ReviveSecondsLeft(this))));
            return;
        }

        if (IsHeld) return;   // in someone's hands the only thing to do with it is put it down

        string refusal = revival != null ? revival.ReviveRefusal(this, interactor) : "nothing can revive it";

        if (refusal.Length == 0)
        {
            // One revive, one prompt, wherever the body is lying. The note only says whether
            // the racket it makes can reach anyone -- it changes nothing about the revive.
            options.Add(new InteractionOption(reviveKey, Revival.IsProtectedSpot(this)
                ? "Revive with adrenaline  (loud, but the generator is holding)"
                : "Revive with adrenaline  (slow and very loud)"));
        }

        int carriedItems = ContentCount;
        if (carriedItems > 0)
        {
            options.Add(new InteractionOption(searchKey,
                carriedItems == 1 ? "Search body  (1 item)" : "Search body  (" + carriedItems + " items)"));
        }

        // The pickup always says why there is no revive yet, so the player is never left
        // guessing what the body wants from them.
        string note = refusal.Length == 0 ? "" : "  (" + refusal + ")";
        options.Add(new InteractionOption(pickUpKey, "Pick up " + itemName + note + PickUpRefusal(interactor)));
    }

    public override void Interact(PlayerInteractor interactor, KeyCode key)
    {
        Revival revival = Revival.Instance;

        if (key == reviveKey && revival != null && revival.IsBeingRevived(this))
        {
            revival.CancelRevive(this);
            return;
        }

        if (!IsHeld && key == reviveKey)
        {
            if (revival != null) revival.TryRevive(this, interactor);
            return;
        }

        if (!IsHeld && key == searchKey)
        {
            Spill();
            return;
        }

        base.Interact(interactor, key);
    }
}
