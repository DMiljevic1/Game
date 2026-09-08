using UnityEngine;

/// <summary>
/// A four-slot pack in the Lethal Company shape: one item in the hands, up to four more
/// stowed on your back, swapped with the number keys.
///
/// It owns the slots and the selection and nothing else. **The hands still belong to
/// PlayerInteractor** -- there is exactly one carried reference in the game and this is
/// not it. Every move an item makes goes through the interactor's existing routes
/// (<see cref="PlayerInteractor.Carry"/>, <see cref="PlayerInteractor.ConsumeCarried"/>,
/// <see cref="PlayerInteractor.DropCarried"/>), so an item is never cloned, destroyed or
/// respawned to get it into a slot -- it is the same GameObject throughout
/// GROUND -> HAND -> SLOT -> HAND -> GROUND.
///
/// The link back to a slot is deliberately derived, not notified: <see cref="HeldFromSlot"/>
/// is only valid while the hands still hold the very item this handed over. So a Q drop, a
/// sale, a death or a fresh pickup all sever it for free, and there is no notification seam
/// that a future item route could forget to call.
///
/// Co-op note: this is per-player local state, exactly like the hands. Slots hold references
/// to ordinary world objects, so replicating it later is a handful of object ids per player.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    public const int SlotCount = 4;

    [Header("Keys")]
    [Tooltip("One per slot, in order. These are read here rather than routed by " +
             "PlayerInteractor because a slot is not something you aim at -- but they are " +
             "still in the project's key map, so nothing else may claim them.")]
    public KeyCode[] slotKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

    [Header("Wiring")]
    [Tooltip("The hands this pack feeds. Found on this object if left empty.")]
    public PlayerInteractor interactor;

    private readonly Carryable[] slots = new Carryable[SlotCount];
    private int selected;

    // What was handed out of a slot, and which slot it came from. Meaningless on its own:
    // always ask through HeldFromSlot, which checks the item is still in the hands.
    private Carryable handedOut;
    private int handedOutSlot = -1;

    /// <summary>Raised whenever a slot or the selection changes. UI only.</summary>
    public event System.Action OnChanged = delegate { };

    /// <summary>The slot the player last selected with a number key.</summary>
    public int Selected { get { return selected; } }

    /// <summary>
    /// Which slot the item in the hands came out of, or -1 if it came off the ground.
    /// Derived, so it can never be stale.
    /// </summary>
    public int HeldFromSlot
    {
        get
        {
            if (interactor == null || handedOut == null) return -1;
            return ReferenceEquals(interactor.Carried, handedOut) ? handedOutSlot : -1;
        }
    }

    /// <summary>What is in a slot, or null. Out-of-range returns null rather than throwing.</summary>
    public Carryable InSlot(int index)
    {
        return (index < 0 || index >= SlotCount) ? null : slots[index];
    }

    /// <summary>True when every slot is occupied.</summary>
    public bool IsFull
    {
        get
        {
            for (int i = 0; i < SlotCount; i++) if (slots[i] == null) return false;
            return true;
        }
    }

    /// <summary>
    /// Could what is in the hands be put away right now? This is what makes a pickup
    /// refuse instead of trading away the thing you are already holding.
    /// </summary>
    public bool CanStowCarried
    {
        get
        {
            Carryable item = interactor == null ? null : interactor.Carried;
            if (item == null) return true;
            return item.canBeStoredInInventory && SlotForStowing() >= 0;
        }
    }

    void Awake()
    {
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (interactor == null)
        {
            Debug.LogError("PlayerInventory on " + name + " has no PlayerInteractor; the pack can never receive an item.", this);
        }

        if (slotKeys == null || slotKeys.Length != SlotCount)
        {
            Debug.LogError("PlayerInventory on " + name + " needs exactly " + SlotCount +
                           " slot keys; slots without one can never be selected.", this);
        }
    }

    void Update()
    {
        if (slotKeys == null) return;

        int count = Mathf.Min(slotKeys.Length, SlotCount);
        for (int i = 0; i < count; i++)
        {
            if (Input.GetKeyDown(slotKeys[i]))
            {
                SelectSlot(i);
                break;   // one slot per frame, so a stuck key cannot chain swaps
            }
        }
    }

    /// <summary>
    /// The whole of the number-key behaviour.
    ///
    /// A slot with something in it is equipped. An empty slot **unequips**: the held item
    /// goes back to the slot it came from, or is stowed in the slot just selected if it
    /// came off the ground.
    ///
    /// **Nothing on this path ever puts an item on the floor.** Unequipping and dropping
    /// are two different actions: once something is in the pack it stays in the player's
    /// possession until they explicitly drop it with <see cref="PlayerInteractor.dropKey"/>.
    /// </summary>
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        if (interactor == null) return;

        selected = index;

        // Something too big to shoulder cannot be swapped away either -- there is nowhere
        // for it to go and we will not drop it for you. It stays in the hands until it is
        // put down by hand.
        Carryable held = interactor.Carried;
        if (held != null && !held.canBeStoredInInventory)
        {
            OnChanged();
            return;
        }

        if (slots[index] != null) EquipFrom(index);
        else Unequip(index);

        OnChanged();
    }

    /// <summary>
    /// Take a slot's item into the hands.
    ///
    /// The slot is vacated **first**, on purpose: that frees somewhere for whatever is
    /// already in the hands, so swapping works even with all four slots full and nothing
    /// ever has to be dropped to make room.
    /// </summary>
    private void EquipFrom(int index)
    {
        Carryable wanted = slots[index];
        slots[index] = null;

        if (!StowCarried())
        {
            // Unreachable: SelectSlot has already turned away anything that cannot be
            // stowed, and we just freed a slot for anything that can. Put it back rather
            // than lose either item.
            slots[index] = wanted;
            Debug.LogError("PlayerInventory could not stow the held item after freeing slot " +
                           index + "; the swap was cancelled.", this);
            return;
        }

        wanted.OnUnstowed();
        interactor.Carry(wanted);   // applies the held pose and the item's weight, as ever

        handedOut = wanted;
        handedOutSlot = index;
    }

    /// <summary>
    /// Empty the hands without equipping anything -- the *unequip* half of the pack, which
    /// is not the same action as dropping and never becomes it.
    ///
    /// An item that came out of a slot goes back to **that** slot, not to the one just
    /// selected: pressing 2 while holding what came out of 1 puts it back in 1 and leaves 2
    /// empty. An item that came off the ground has no slot of its own yet, so the empty slot
    /// the player pointed at is where it goes -- that is how something gets deliberately
    /// stored rather than only by being bumped out of the hands by the next pickup.
    ///
    /// Either way the item stays in the player's possession. Only the drop key puts it on
    /// the floor.
    /// </summary>
    private void Unequip(int selectedSlot)
    {
        Carryable item = interactor.Carried;
        if (item == null) return;

        int origin = HeldFromSlot;
        if (origin >= 0 && slots[origin] == null)
        {
            MoveCarriedIntoSlot(origin);
            return;
        }

        if (item.canBeStoredInInventory && slots[selectedSlot] == null)
        {
            MoveCarriedIntoSlot(selectedSlot);
            return;
        }

        // Nowhere legal for it. Keep holding it rather than putting it down uninvited.
    }

    /// <summary>
    /// Put whatever is in the hands into a slot. Returns false only when there is nowhere
    /// for it to go -- in which case the hands are left exactly as they were.
    ///
    /// Called by <see cref="PlayerInteractor.Carry"/> when the player picks something up
    /// with full hands, which is why a pickup can refuse rather than drop what you hold.
    /// </summary>
    public bool StowCarried()
    {
        Carryable item = interactor == null ? null : interactor.Carried;
        if (item == null) return true;                        // nothing to put away
        if (!item.canBeStoredInInventory) return false;       // too big for a slot, ever

        int slot = SlotForStowing();
        if (slot < 0) return false;

        MoveCarriedIntoSlot(slot);
        return true;
    }

    /// <summary>The one place an item goes from the hands into a slot.</summary>
    private void MoveCarriedIntoSlot(int slot)
    {
        Carryable item = interactor.Carried;

        // Out of the hands without being placed in the world and without being destroyed.
        // This also clears the carry weight, because every route through the interactor does.
        interactor.ConsumeCarried();

        item.OnStowed();
        slots[slot] = item;
        ClearHandedOut();
    }

    /// <summary>
    /// Where the held item should go. Back where it came from if that slot is still free --
    /// taking the watch out of slot 3 and then picking something up should not shuffle the
    /// watch to slot 1 -- otherwise the first empty slot.
    /// </summary>
    private int SlotForStowing()
    {
        int origin = HeldFromSlot;
        if (origin >= 0 && slots[origin] == null) return origin;

        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] == null) return i;
        }
        return -1;
    }

    private void ClearHandedOut()
    {
        handedOut = null;
        handedOutSlot = -1;
    }

    /// <summary>
    /// Forget a slot's contents without dropping or destroying anything. Here for a future
    /// item that leaves the world by some other route; a slot whose item is destroyed
    /// already reads as empty, because Unity's == treats a destroyed object as null.
    /// </summary>
    public void ClearSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        slots[index] = null;
        OnChanged();
    }
}
