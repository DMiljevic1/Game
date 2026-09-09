using UnityEngine;

/// <summary>
/// The store panel: one row per item, the balance, and a Buy button.
///
/// It only *reads* the <see cref="Store"/> and the <see cref="Wallet"/> and asks the
/// store to perform a purchase -- no price, no arithmetic and no stock lives here, the
/// same rule MoneyHud and InventoryHud follow. Deleting this component would cost the
/// player the shopfront, never the shop: <see cref="Store.TryBuy"/> would still work.
///
/// Being immediate-mode it is redrawn every frame, so an affordable item becomes
/// affordable on screen the instant a sale pays out, with nothing to refresh.
/// </summary>
[DisallowMultipleComponent]
public class StoreHud : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The store to show. Found on this object if left empty.")]
    public Store store;

    [Tooltip("Whose balance to show. Falls back to the store's wallet, then Wallet.Instance.")]
    public Wallet wallet;

    [Header("Look")]
    [Tooltip("Panel width as a fraction of the screen.")]
    [Range(0.3f, 0.9f)] public float widthFraction = 0.46f;

    public Color background = new Color(0.04f, 0.04f, 0.05f, 0.92f);
    public Color border = new Color(1f, 0.92f, 0.6f, 0.8f);
    public Color rowBackground = new Color(1f, 1f, 1f, 0.05f);
    public Color titleText = new Color(1f, 0.92f, 0.6f);
    public Color itemText = new Color(1f, 0.97f, 0.85f);
    public Color noteText = new Color(1f, 1f, 1f, 0.45f);
    public Color affordText = new Color(0.55f, 1f, 0.6f);
    public Color tooDearText = new Color(1f, 0.5f, 0.45f);

    void Awake()
    {
        if (store == null) store = GetComponent<Store>();
        if (store == null)
        {
            Debug.LogError("StoreHud on " + name + " has no Store; there is nothing to draw.", this);
            enabled = false;
            return;
        }

        if (wallet == null) wallet = store.wallet;
    }

    void OnGUI()
    {
        if (store == null || !store.IsOpen) return;

        if (wallet == null) wallet = store.wallet != null ? store.wallet : Wallet.Instance;
        int money = wallet != null ? wallet.Money : 0;

        float line = Hud.LineHeight;
        float rowHeight = line * 2.3f;
        float pad = line * 0.6f;

        float width = Screen.width * widthFraction;
        float height = pad * 2f + line * 2.6f + store.stock.Count * (rowHeight + pad * 0.4f) + line * 1.8f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

        Hud.Box(panel, background);
        Hud.Frame(panel, border, 2f);

        float y = panel.y + pad;

        // Header: what this is, and what the team can spend.
        Hud.Label(new Rect(panel.x + pad, y, width * 0.5f, line), "STORE", Hud.Readout, titleText);
        Hud.Label(new Rect(panel.x + pad, y, width - pad * 2f, line),
                  "$" + money.ToString("N0"), Hud.ReadoutRight, new Color(1f, 0.86f, 0.35f));
        y += line * 1.6f;

        for (int i = 0; i < store.stock.Count; i++)
        {
            Rect row = new Rect(panel.x + pad, y, width - pad * 2f, rowHeight);
            DrawRow(i, row, money);
            y += rowHeight + pad * 0.4f;
        }

        // Feedback lives on the panel, under the last row: a refusal has to be next to
        // the button that refused, not in a corner the player is not looking at.
        string message = store.Message;
        if (message.Length > 0)
        {
            Hud.Label(new Rect(panel.x + pad, y, width - pad * 2f, line), message, Hud.Readout,
                      store.MessageIsError ? tooDearText : affordText);
        }
        else
        {
            Hud.Label(new Rect(panel.x + pad, y, width - pad * 2f, line),
                      "[" + store.browseKey + "] or [" + store.closeKey + "] to leave", Hud.Readout, noteText);
        }
    }

    private void DrawRow(int index, Rect row, int money)
    {
        StoreItem item = store.stock[index];
        if (item == null) return;

        Hud.Box(row, rowBackground);

        float line = Hud.LineHeight;
        float buttonWidth = row.width * 0.24f;
        float priceWidth = row.width * 0.20f;
        float textWidth = row.width - buttonWidth - priceWidth - line;

        Hud.Label(new Rect(row.x + line * 0.4f, row.y + line * 0.15f, textWidth, line),
                  item.Label, Hud.Readout, itemText);

        if (item.note.Length > 0)
        {
            Hud.Label(new Rect(row.x + line * 0.4f, row.y + line * 1.15f, textWidth, line),
                      item.note, Hud.SlotLabel, noteText);
        }

        bool affordable = store.Shortfall(item) == 0;
        Hud.Label(new Rect(row.x + textWidth, row.y + (row.height - line) * 0.5f, priceWidth, line),
                  "$" + item.price.ToString("N0"), Hud.ReadoutRight,
                  affordable ? affordText : tooDearText);

        Rect button = new Rect(row.xMax - buttonWidth - line * 0.4f, row.y + row.height * 0.2f,
                               buttonWidth, row.height * 0.6f);

        // An unaffordable item keeps a live button, dimmed rather than disabled: pressing
        // it is how the player learns how much short they are, which is more use than a
        // dead control that explains nothing.
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = affordable ? previous : new Color(1f, 1f, 1f, 0.4f);

        if (GUI.Button(button, "Buy", Hud.Button))
        {
            store.TryBuy(index, store.Browser);
        }

        GUI.backgroundColor = previous;
    }
}
