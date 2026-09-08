using UnityEngine;

/// <summary>
/// The four slots along the bottom of the screen.
///
/// It only *reads* <see cref="PlayerInventory"/>. No slot, key, swap or drop logic lives
/// here -- deleting this costs the readout and never the inventory, the same rule
/// MoneyHud follows for the wallet. Being immediate-mode, it is redrawn every frame, so
/// it is always current: a pickup, a stow, an equip, a return and a drop all show the
/// instant they happen without anything having to remember to refresh it.
///
/// Placeholder art, like the rest of the HUD -- but readable at night, which is the part
/// that matters for actually testing the game.
/// </summary>
[DisallowMultipleComponent]
public class InventoryHud : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The pack to show. Found on this object if left empty.")]
    public PlayerInventory inventory;

    [Tooltip("Used only to mark which slot the item in the hands came out of. " +
             "Found on this object if left empty.")]
    public PlayerInteractor interactor;

    [Header("Look")]
    [Tooltip("Slot width as a multiple of its height. Wider than tall, so an item name fits.")]
    public float slotAspect = 1.45f;

    public Color emptyBackground = new Color(0f, 0f, 0f, 0.45f);
    public Color filledBackground = new Color(0.16f, 0.15f, 0.11f, 0.70f);
    public Color selectedBackground = new Color(1f, 0.92f, 0.6f, 0.22f);
    public Color border = new Color(1f, 1f, 1f, 0.30f);
    public Color selectedBorder = new Color(1f, 0.92f, 0.6f, 0.95f);
    public Color itemText = new Color(1f, 0.97f, 0.85f);
    public Color emptyText = new Color(1f, 1f, 1f, 0.28f);

    void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();

        if (inventory == null)
        {
            Debug.LogError("InventoryHud on " + name + " has no PlayerInventory; there is nothing to draw.", this);
            enabled = false;
        }
    }

    void OnGUI()
    {
        if (inventory == null) return;

        float bar = Hud.BottomBarHeight;
        float height = bar * 0.74f;
        float width = height * slotAspect;
        float gap = height * 0.14f;

        float total = PlayerInventory.SlotCount * width + (PlayerInventory.SlotCount - 1) * gap;
        float x = (Screen.width - total) * 0.5f;
        float y = Screen.height - bar + (bar - height) * 0.5f;

        int heldFrom = inventory.HeldFromSlot;

        for (int i = 0; i < PlayerInventory.SlotCount; i++)
        {
            Rect slot = new Rect(x + i * (width + gap), y, width, height);
            Draw(i, slot, heldFrom);
        }
    }

    private void Draw(int index, Rect slot, int heldFrom)
    {
        Carryable item = inventory.InSlot(index);
        bool selected = inventory.Selected == index;

        Color background = selected ? selectedBackground : (item != null ? filledBackground : emptyBackground);
        Hud.Box(slot, background);
        Hud.Frame(slot, selected ? selectedBorder : border, selected ? 3f : 2f);

        // The key that selects this slot, so the binding never has to be already known.
        Hud.Label(new Rect(slot.x + 6f, slot.y + 3f, slot.width, Hud.SlotKey.fontSize * 1.3f),
                  (index + 1).ToString(), Hud.SlotKey,
                  selected ? selectedBorder : new Color(1f, 1f, 1f, 0.55f));

        Rect text = new Rect(slot.x + 4f, slot.y + Hud.SlotKey.fontSize * 1.1f,
                             slot.width - 8f, slot.height - Hud.SlotKey.fontSize * 1.4f);

        if (item != null)
        {
            Hud.Label(text, item.itemName, Hud.SlotLabel, itemText);
            return;
        }

        // An empty slot that the held item will go back to is worth saying out loud: it is
        // the answer to "where does this end up if I press another number?".
        if (heldFrom == index)
        {
            Hud.Label(text, "(in hand)", Hud.SlotLabel, new Color(1f, 0.92f, 0.6f, 0.75f));
            return;
        }

        Hud.Label(text, "-", Hud.SlotLabel, emptyText);
    }
}
