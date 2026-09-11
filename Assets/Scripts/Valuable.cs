using System.Collections.Generic;
using UnityEngine;

/// <summary>How bulky a piece of loot is. The category is the tuning dial: change what
/// Large costs once and every large item in the game changes with it.</summary>
public enum ValuableSize
{
    /// <summary>Pocketable. No penalty at all -- grab it and run.</summary>
    Small,

    /// <summary>Two hands, but you can still run. Noticeable, not annoying.</summary>
    Medium,

    /// <summary>Hugged to the chest. Slow, and no running at all.</summary>
    Large,

    /// <summary>Ignore the presets; the multipliers on the Carryable are used as authored.</summary>
    Custom
}

/// <summary>
/// Loot: something found out in the dark that is worth money at the sell station.
///
/// It is a plain Carryable, so it is picked up and dropped by the existing
/// interaction router with no special handling. All it adds is a price and a size,
/// which means a new valuable is a new prefab and two numbers, never a new script.
///
/// Price and size together are the whole risk/reward decision: the $300 television
/// is the one you cannot run away with.
/// </summary>
[DisallowMultipleComponent]
public class Valuable : Carryable
{
    [Header("Worth")]
    [Tooltip("What the sell station pays for this, in whole dollars.")]
    public int value = 100;

    [Header("Bulk")]
    [Tooltip("Which movement preset applies. Custom leaves the multipliers on the Carryable alone.")]
    public ValuableSize size = ValuableSize.Small;

    protected override void Awake()
    {
        // Before base.Awake, so the load is already right if anything reads it early.
        ApplySizePreset();
        base.Awake();

        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "valuable";

        if (value <= 0)
        {
            Debug.LogWarning("Valuable " + name + " is worth " + value + "; it will sell for nothing.", this);
        }
    }

    // Keeps the Inspector honest: pick a size and the multipliers below it update, so
    // what you read on the component is what the player will actually feel.
    void OnValidate()
    {
        ApplySizePreset();
    }

    /// <summary>
    /// The one place a size becomes numbers. Retune a category here and every item in
    /// it follows; an item that needs its own feel is set to Custom instead.
    /// </summary>
    public void ApplySizePreset()
    {
        switch (size)
        {
            case ValuableSize.Small:
                carryMoveMultiplier = 1f;
                carrySprintMultiplier = 1f;
                allowSprintWhileCarried = true;
                canBeStoredInInventory = true;
                break;

            case ValuableSize.Medium:
                carryMoveMultiplier = 0.9f;
                carrySprintMultiplier = 0.9f;
                allowSprintWhileCarried = true;
                canBeStoredInInventory = true;
                break;

            case ValuableSize.Large:
                // Carrying the television home past something hunting you is the gamble.
                // Shared with Tom's case, so the two can never drift apart.
                ApplyLargeLoad();
                break;

            case ValuableSize.Custom:
                break;                            // authored by hand; leave it alone
        }
    }

    // Show the price on the pickup prompt: worth is the whole reason to carry it
    // home, so the player should not have to guess before making the trip. The size
    // rides along with it, because the cost of picking it up is half the decision.
    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld) return;

        string note = size == ValuableSize.Small ? "" : (allowSprintWhileCarried ? " - heavy" : " - no sprint");
        options.Add(new InteractionOption(pickUpKey,
            string.Format("Pick up {0} (${1:N0}{2}){3}", itemName, value, note, PickUpRefusal(interactor))));
    }
}
