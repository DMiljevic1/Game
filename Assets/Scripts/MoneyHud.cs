using UnityEngine;

/// <summary>
/// Draws the balance in the top-right corner and a brief "+$100" when it changes.
///
/// It only reads the Wallet and reacts to its event: no sale, no price and no
/// balance arithmetic happens here. Deleting this component would cost the player
/// the readout, never the money.
/// </summary>
[DisallowMultipleComponent]
public class MoneyHud : MonoBehaviour
{
    [Tooltip("Whose balance to show. Falls back to Wallet.Instance if left empty.")]
    public Wallet wallet;

    [Tooltip("How long the change popup stays on screen.")]
    public float popupSeconds = 2.2f;

    private int lastDelta;
    private float popupEndsAt = -1f;

    void OnEnable()
    {
        if (wallet == null) wallet = Wallet.Instance;
        if (wallet != null) wallet.OnMoneyChanged += HandleMoneyChanged;
    }

    void OnDisable()
    {
        if (wallet != null) wallet.OnMoneyChanged -= HandleMoneyChanged;
    }

    void Start()
    {
        if (wallet == null)
        {
            Debug.LogError("MoneyHud on " + name + " found no Wallet; the balance will read $0 forever.", this);
        }
    }

    private void HandleMoneyChanged(int total, int delta)
    {
        lastDelta = delta;
        popupEndsAt = Time.time + popupSeconds;
    }

    void OnGUI()
    {
        int total = wallet != null ? wallet.Money : 0;

        // Right column, row 0: the balance. Left-hand rows are claimed elsewhere.
        Hud.RowRight(0, "$" + total.ToString("N0"), new Color(1f, 0.86f, 0.35f));

        if (lastDelta == 0 || Time.time >= popupEndsAt) return;

        // Hold at full strength, then fade over the last third: long enough to read,
        // short enough that it is gone before the player looks away.
        float remaining = (popupEndsAt - Time.time) / popupSeconds;
        Color tint = lastDelta > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.45f);
        tint.a = Mathf.Clamp01(remaining * 3f);

        string sign = lastDelta > 0 ? "+$" : "-$";
        Hud.RowRight(1, sign + Mathf.Abs(lastDelta).ToString("N0"), tint);
    }
}
