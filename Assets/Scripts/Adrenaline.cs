using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The revival item. Bought at the store and carried like anything else; it does nothing
/// on its own. A <see cref="PlayerBody"/> offers the revive when you look at it holding
/// one, and <see cref="Revival"/> spends it -- so the item owns no revive logic at all.
///
/// Its store price is not a number on the stock list: it asks <see cref="Revival"/>,
/// which prices it from how many revives the run has used.
///
/// Co-op note: it is an ordinary world object with no per-player state, so it can be
/// handed to, dropped for or stolen by anyone on the team.
/// </summary>
[DisallowMultipleComponent]
public class Adrenaline : Carryable, IStorePriced
{
    // Every unspent dose in the level, stowed ones included -- Awake/OnDestroy rather than
    // OnEnable/OnDisable, because stowing deactivates the object and a dose in a pack is
    // still a dose. Scene-object registry, not per-player state.
    private static readonly List<Adrenaline> unspent = new List<Adrenaline>();
    private bool spent;

    /// <summary>Doses bought but not yet used, wherever they are.</summary>
    public static int UnspentCount { get { return unspent.Count; } }

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "adrenaline";
        unspent.Add(this);
    }

    void OnDestroy()
    {
        unspent.Remove(this);
    }

    /// <summary>
    /// Used up on a body. Leaves the count immediately rather than at end of frame, so the
    /// price can never briefly count both the dose and the revive it paid for.
    /// </summary>
    public void Spend()
    {
        if (spent) return;
        spent = true;

        unspent.Remove(this);
        Destroy(gameObject);
    }

    /// <summary>What the store charges: the current revive price, or the listed one if there is no Revival.</summary>
    public int StorePrice(int listedPrice)
    {
        Revival revival = Revival.Instance;
        return revival != null ? revival.NextRevivePrice : listedPrice;
    }
}
