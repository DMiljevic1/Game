using UnityEngine;

/// <summary>
/// The revival item. Bought at the store and carried like anything else; it does nothing
/// on its own. A <see cref="PlayerBody"/> offers the revive when you look at it holding
/// one, and <see cref="Revival"/> spends it -- so the item owns no revive logic at all.
///
/// Its store price is not a number on the stock list: it asks <see cref="Revival"/>,
/// which holds the one flat revive price.
///
/// Co-op note: it is an ordinary world object with no per-player state, so it can be
/// handed to, dropped for or stolen by anyone on the team.
/// </summary>
[DisallowMultipleComponent]
public class Adrenaline : Carryable, IStorePriced
{
    private bool spent;

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "adrenaline";
    }

    /// <summary>Used up on a body. Guarded, so two routes can never spend the same dose.</summary>
    public void Spend()
    {
        if (spent) return;
        spent = true;

        Destroy(gameObject);
    }

    /// <summary>What the store charges: the current revive price, or the listed one if there is no Revival.</summary>
    public int StorePrice(int listedPrice)
    {
        Revival revival = Revival.Instance;
        return revival != null ? revival.NextRevivePrice : listedPrice;
    }
}
