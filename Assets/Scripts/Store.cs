using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One line of stock: what it is called, what it costs, and what to hand over.
/// A new purchasable is a new prefab and one number, the same shape adding a
/// Valuable to the loot table has -- there is no per-item script to write.
/// </summary>
[System.Serializable]
public class StoreItem
{
    [Tooltip("Shown on the store panel. Falls back to the prefab's Carryable name if left empty.")]
    public string displayName = "";

    public int price = 100;

    [Tooltip("Instantiated on purchase. Must carry a Carryable -- the store hands the item " +
             "to PlayerInteractor.Carry, exactly as picking one off the floor does.")]
    public GameObject prefab;

    [Tooltip("One short line under the name. Purely descriptive.")]
    public string note = "";

    /// <summary>The name to print, taken from the prefab when not overridden.</summary>
    public string Label
    {
        get
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            if (prefab == null) return "(nothing)";

            Carryable c = prefab.GetComponent<Carryable>();
            return c != null ? c.itemName : prefab.name;
        }
    }
}

/// <summary>Why a purchase did or did not happen. The HUD turns this into a sentence.</summary>
public enum StoreResult
{
    Bought,
    NotEnoughMoney,
    NoRoom,
    Unavailable
}

/// <summary>
/// The shop counter in the safe house: money turns back into equipment.
///
/// It is the mirror image of <see cref="SellStation"/> and shares its rules. It is an
/// ordinary IInteractable, so PlayerInteractor does the aiming, the prompting and the
/// key routing; the Wallet stays the single authority on money; and a bought item reaches
/// the player through <see cref="PlayerInteractor.Carry"/> -- the one existing route into
/// their possession, which already stows what they are holding and already refuses when
/// hands and pack are both full.
///
/// Nothing sold here is a Valuable and nothing here is in LootSpawner's table, so store
/// goods can never turn up lying in the world. That is not a flag anyone has to remember
/// to set; it is simply the only way an item ever spawns.
///
/// Co-op note: the purchase is authority-only and the Wallet is shared by the team,
/// exactly like a sale. Browsing is per-player local state, like the hands.
/// </summary>
[DisallowMultipleComponent]
public class Store : MonoBehaviour, IInteractable
{
    [Header("Keys")]
    [Tooltip("Opens and closes the store panel. Declared here, so nothing can silently steal it.")]
    public KeyCode browseKey = KeyCode.E;

    [Tooltip("Also closes the panel, for the reflex that reaches for it.")]
    public KeyCode closeKey = KeyCode.Escape;

    [Header("Stock")]
    [Tooltip("Everything for sale, in the order it is listed. Add an entry to add an item.")]
    public List<StoreItem> stock = new List<StoreItem>();

    [Header("Wiring")]
    [Tooltip("Where the money comes from. Falls back to Wallet.Instance if left empty.")]
    public Wallet wallet;

    [Tooltip("Where a bought item appears before it goes into the player's hands. " +
             "The store's own position is used if left empty.")]
    public Transform deliveryPoint;

    [Header("Feedback")]
    [Tooltip("How long the last purchase message stays on the panel.")]
    public float messageSeconds = 3f;

    [Tooltip("Pulsed on a successful purchase, so the confirmation exists in the world " +
             "and not only on the panel.")]
    public Light confirmLight;
    public float idleIntensity = 1.4f;
    public float saleIntensity = 8f;
    public float flashSeconds = 0.55f;

    [Header("Noise")]
    [Tooltip("How far the clatter of a purchase carries. Buying is not a routed key, so " +
             "the noise the interactor emits for everything else does not cover it.")]
    public float purchaseNoiseRadius = 8f;

    /// <summary>Item name and price, for anything that wants to react to a purchase.</summary>
    public event System.Action<string, int> OnBought = delegate { };

    // Browsing state. Per-player and local: the panel is a view of shared money, never
    // an owner of it.
    private PlayerInteractor browser;
    private MouseLook suspendedLook;
    private PlayerMovement suspendedMovement;
    private CursorLockMode previousLock;
    private bool previousCursorVisible;

    private string message = "";
    private bool messageIsError;
    private float messageEndsAt = -1f;
    private float flashEndsAt = -1f;

    // Authority seam, as with TimeOfDay, Generator, Wallet and LootSpawner.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>True while the panel is up. The HUD draws only then.</summary>
    public bool IsOpen { get { return browser != null; } }

    /// <summary>Who is browsing, or null. The HUD needs it to ask what they can carry.</summary>
    public PlayerInteractor Browser { get { return browser; } }

    /// <summary>The last purchase message, or "" once it has expired.</summary>
    public string Message { get { return Time.time < messageEndsAt ? message : ""; } }

    /// <summary>True when <see cref="Message"/> is a refusal rather than a confirmation.</summary>
    public bool MessageIsError { get { return messageIsError; } }

    void Start()
    {
        if (wallet == null) wallet = Wallet.Instance;
        if (wallet == null)
        {
            Debug.LogError("Store on " + name + " has no Wallet; nothing can ever be bought.", this);
        }
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("Store on " + name + " has no collider, so it can never be looked at.", this);
        }

        for (int i = 0; i < stock.Count; i++)
        {
            if (stock[i] == null || stock[i].prefab == null)
            {
                Debug.LogError("Store on " + name + " has an entry with no prefab at index " + i +
                               "; it can be listed but never bought.", this);
                continue;
            }
            if (stock[i].prefab.GetComponent<Carryable>() == null)
            {
                Debug.LogError("Store item '" + stock[i].Label + "' has no Carryable, so the player " +
                               "could never hold it.", this);
            }
        }

        ApplyFlash();
    }

    void OnDisable()
    {
        // Never leave the player unable to look around because the store went away.
        Close();
    }

    // ------------------------------------------------------------- interaction

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        options.Add(new InteractionOption(browseKey, IsOpen ? "Close store" : "Browse store"));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != browseKey) return;

        if (IsOpen) Close();
        else Open(interactor);
    }

    void Update()
    {
        ApplyFlash();

        if (!IsOpen) return;

        // The browser can stop existing (a scene change, a disabled player) without the
        // panel ever being closed, and a locked-away cursor is the worst thing to leave behind.
        if (browser == null || !browser.isActiveAndEnabled)
        {
            Close();
            return;
        }

        if (Input.GetKeyDown(closeKey)) Close();
    }

    // ------------------------------------------------------------- the panel

    /// <summary>
    /// Put the panel up for one player.
    ///
    /// Buying is done with the mouse, so the cursor has to come back -- which means
    /// suspending mouse look, and holding the player still so they cannot walk out of the
    /// shop they are standing in. Both are switched off from the outside and restored to
    /// exactly what they were, so neither script has to know the store exists.
    /// </summary>
    public void Open(PlayerInteractor interactor)
    {
        if (interactor == null || IsOpen) return;

        browser = interactor;

        suspendedLook = interactor.viewCamera != null ? interactor.viewCamera.GetComponent<MouseLook>() : null;
        if (suspendedLook != null && !suspendedLook.enabled) suspendedLook = null;   // already off: not ours to turn on
        if (suspendedLook != null) suspendedLook.enabled = false;

        suspendedMovement = interactor.movement;
        if (suspendedMovement != null && !suspendedMovement.enabled) suspendedMovement = null;
        if (suspendedMovement != null) suspendedMovement.enabled = false;

        previousLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Take the panel down and give back only what <see cref="Open"/> took away.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        if (suspendedLook != null) suspendedLook.enabled = true;
        if (suspendedMovement != null) suspendedMovement.enabled = true;

        Cursor.lockState = previousLock;
        Cursor.visible = previousCursorVisible;

        suspendedLook = null;
        suspendedMovement = null;
        browser = null;
    }

    // ------------------------------------------------------------- buying

    /// <summary>What the player is short by, or 0 if they can afford it.</summary>
    public int Shortfall(StoreItem item)
    {
        if (item == null) return 0;

        int have = wallet != null ? wallet.Money : 0;
        return Mathf.Max(0, item.price - have);
    }

    /// <summary>
    /// Buy one line of stock and put it in the player's possession.
    ///
    /// The money is spent only once the item is certain to have somewhere to go, so a
    /// refused pickup can never leave the player poorer with nothing to show for it.
    /// </summary>
    public StoreResult TryBuy(int index, PlayerInteractor interactor)
    {
        if (!HasAuthority) return StoreResult.Unavailable;   // clients ask the server, they do not mint items

        StoreItem item = (index < 0 || index >= stock.Count) ? null : stock[index];
        if (item == null || item.prefab == null || wallet == null || interactor == null)
        {
            return Report(StoreResult.Unavailable, "Out of stock.");
        }

        int owed = Shortfall(item);
        if (owed > 0)
        {
            return Report(StoreResult.NotEnoughMoney, string.Format("Not enough money - ${0:N0} short.", owed));
        }

        // The same question a pickup off the floor asks, so the store cannot force an item
        // into hands that are full in a way nothing else could.
        if (!interactor.CanPickUp)
        {
            return Report(StoreResult.NoRoom, "No room - " + interactor.PickUpRefusalReason + ".");
        }

        Vector3 where = deliveryPoint != null ? deliveryPoint.position : transform.position + Vector3.up;
        GameObject spawned = Instantiate(item.prefab, where, Quaternion.identity);
        spawned.name = item.prefab.name;

        Carryable carryable = spawned.GetComponent<Carryable>();
        if (carryable == null)
        {
            Debug.LogError("Store item '" + item.Label + "' has no Carryable; the purchase was cancelled.", this);
            Destroy(spawned);
            return Report(StoreResult.Unavailable, "Out of stock.");
        }

        wallet.Add(-item.price);
        interactor.Carry(carryable);

        // Working the counter is audible, on the same footing as every other interaction --
        // the interactor only emits for keys it routed, and a button click is not one.
        Noise.Emit(transform.position, purchaseNoiseRadius, gameObject);

        flashEndsAt = Time.time + flashSeconds;
        OnBought(item.Label, item.price);

        return Report(StoreResult.Bought, "Bought " + item.Label + ".");
    }

    private StoreResult Report(StoreResult result, string text)
    {
        message = text;
        messageIsError = result != StoreResult.Bought;
        messageEndsAt = Time.time + messageSeconds;
        return result;
    }

    private void ApplyFlash()
    {
        if (confirmLight == null) return;

        float remaining = flashEndsAt - Time.time;
        float t = flashSeconds <= 0f ? 0f : Mathf.Clamp01(remaining / flashSeconds);
        confirmLight.intensity = Mathf.Lerp(idleIntensity, saleIntensity, t);
    }
}
