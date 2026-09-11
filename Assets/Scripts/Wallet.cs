using UnityEngine;

/// <summary>
/// The single authority on how much money the players have. Everything that pays
/// out goes through <see cref="Add"/>; nothing else writes the balance.
///
/// Co-op note: this is deliberately one object owning one piece of shared state,
/// the same shape as RunState. Only the authority mints money - a client mirrors
/// the value it is told via <see cref="SetMoney"/>. Money is shared by the team,
/// not per-player, so there is exactly one Wallet in the level.
/// </summary>
[DisallowMultipleComponent]
public class Wallet : MonoBehaviour
{
    public static Wallet Instance { get; private set; }

    [Tooltip("Money in hand. The shop will read this later.")]
    public int money = 0;

    /// <summary>New balance, and the change that produced it.</summary>
    public event System.Action<int, int> OnMoneyChanged = delegate { };

    public int Money { get { return money; } }

    // Authority seam. Becomes IsServer/IsHost once netcode is in; until then the
    // local game is the authority.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second Wallet exists on " + name + "; destroying it. There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Pay the players. Returns the new balance.</summary>
    public int Add(int amount)
    {
        if (amount == 0) return money;
        if (!HasAuthority) return money;   // clients are told the balance, they do not mint it

        money += amount;
        OnMoneyChanged(money, amount);
        return money;
    }

    /// <summary>
    /// Overwrite the balance. Normal play never calls this: it exists so a networked
    /// client can mirror the authority's value, and so the editor can preview.
    /// </summary>
    public void SetMoney(int value)
    {
        int delta = value - money;
        if (delta == 0) return;

        money = value;
        OnMoneyChanged(money, delta);
    }
}
