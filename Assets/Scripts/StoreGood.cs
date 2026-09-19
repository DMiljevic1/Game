using UnityEngine;

/// <summary>
/// Stamped on an item by <see cref="Store.TryBuy"/>: what it was bought for, and so what
/// the sell counter will give back for it. Added at purchase rather than authored on the
/// prefab, so only something that actually came over the counter can go back over it --
/// the fuel cans lying in the yard were never paid for and are not worth anything.
///
/// It travels with the GameObject through hands, pack and a dead player's body, because
/// none of those routes ever clone or respawn an item.
/// </summary>
[DisallowMultipleComponent]
public class StoreGood : MonoBehaviour
{
    [Tooltip("What the sell counter pays for this. Set by the store at purchase.")]
    public int resaleValue;
}
