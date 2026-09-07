using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The counter in the house where loot turns into money.
///
/// It is an ordinary IInteractable, so PlayerInteractor does the aiming, the
/// prompting and the key routing - exactly like the generator, which only offers
/// refuel when you are actually holding a can. Here: no valuable in hand, no
/// prompt at all.
///
/// Co-op note: the station performs the sale and the Wallet records it. Nothing
/// about the transaction lives in the UI, so a client showing the balance can be
/// wrong without the balance itself being wrong.
/// </summary>
[DisallowMultipleComponent]
public class SellStation : MonoBehaviour, IInteractable
{
    [Header("Keys")]
    public KeyCode sellKey = KeyCode.E;

    [Header("Wiring")]
    [Tooltip("Where the money goes. Falls back to Wallet.Instance if left empty.")]
    public Wallet wallet;

    [Header("Feedback")]
    [Tooltip("Pulsed on a successful sale, so the confirmation exists in the world and not only on the HUD.")]
    public Light confirmLight;
    public float idleIntensity = 1.6f;
    public float saleIntensity = 9f;
    public float flashSeconds = 0.55f;

    /// <summary>Item name and price, for anything that wants to react to a sale.</summary>
    public event System.Action<string, int> OnSold = delegate { };

    private float flashEndsAt = -1f;

    void Start()
    {
        if (wallet == null) wallet = Wallet.Instance;
        if (wallet == null)
        {
            Debug.LogError("SellStation on " + name + " has no Wallet; selling will do nothing.", this);
        }
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("SellStation on " + name + " has no collider, so it can never be looked at.", this);
        }
        ApplyFlash();
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        Valuable item = interactor.GetCarried<Valuable>();
        if (item == null) return;   // nothing worth selling in hand: offer nothing

        options.Add(new InteractionOption(sellKey, string.Format("Sell {0} (${1:N0})", item.itemName, item.value)));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != sellKey) return;

        Valuable item = interactor.GetCarried<Valuable>();
        if (item == null) return;

        // Read the price before the item goes away.
        string soldName = item.itemName;
        int paid = item.value;

        // Out of the hands without being dropped on the floor, then out of the world.
        interactor.ConsumeCarried();
        Destroy(item.gameObject);

        if (wallet != null) wallet.Add(paid);
        OnSold(soldName, paid);

        flashEndsAt = Time.time + flashSeconds;
    }

    void Update()
    {
        ApplyFlash();
    }

    private void ApplyFlash()
    {
        if (confirmLight == null) return;

        float remaining = flashEndsAt - Time.time;
        float t = flashSeconds <= 0f ? 0f : Mathf.Clamp01(remaining / flashSeconds);
        confirmLight.intensity = Mathf.Lerp(idleIntensity, saleIntensity, t);
    }
}
