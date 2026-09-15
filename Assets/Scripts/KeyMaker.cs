using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The key maker on the kitchen table: the one place the four <see cref="KeyFragment"/>s
/// become a <see cref="CompleteKey"/>.
///
/// An ordinary IInteractable in the same shape as the generator's refuel and the sell
/// counter -- it offers nothing unless your hands make something possible, so it sits there
/// as furniture until you come back with a fragment.
///
/// **The count is the whole UI.** A fitted fragment is pinned into one of the four sockets
/// and stays there, so how far along you are is visible on the device itself; the prompt and
/// one HUD line say it in words as well, because "3/4" is the one number in this level the
/// player genuinely needs. When the fourth goes in the fragments are consumed and the
/// finished key is put down on the table's output point for the player to pick up.
///
/// Co-op note: the fitted count is shared state owned by the authority, the same shape as
/// Wallet. Any player can carry any fragment to it, and any player can take the key.
/// </summary>
[DisallowMultipleComponent]
public class KeyMaker : MonoBehaviour, IInteractable
{
    [Tooltip("Fits the fragment in your hands. Declared here, so nothing can silently steal it.")]
    public KeyCode fitKey = KeyCode.E;

    [Tooltip("How many fragments the key is made of. Kept in step with Expedition.fragmentsInLevel.")]
    public int fragmentsNeeded = 4;

    [Tooltip("Where fitted fragments are shown. One per fragment; a missing one just means " +
             "nothing is drawn for it.")]
    public Transform[] sockets;

    [Tooltip("What the finished key is made from. Needs a CompleteKey.")]
    public GameObject keyPrefab;

    [Tooltip("Where the finished key is laid down. This object's transform if left empty.")]
    public Transform output;

    [Tooltip("Lights that come on as the device fills, one per fragment. Optional.")]
    public Light[] indicatorLights;

    /// <summary>Fired whenever a fragment goes in, and again when the key is made. UI only.</summary>
    public event System.Action OnChanged = delegate { };

    private readonly List<KeyFragment> fitted = new List<KeyFragment>();
    private CompleteKey madeKey;

    /// <summary>How many fragments are in the device.</summary>
    public int Fitted { get { return fitted.Count; } }

    /// <summary>How many it wants in total.</summary>
    public int Needed { get { return Mathf.Max(1, fragmentsNeeded); } }

    /// <summary>True once the key has been made. It only ever happens once.</summary>
    public bool KeyMade { get { return madeKey != null || keyWasMade; } }

    private bool keyWasMade;

    // Authority seam, as with Wallet and Store.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("KeyMaker on " + name + " has no collider, so it can never be looked at.", this);
        }
        if (keyPrefab == null)
        {
            Debug.LogError("KeyMaker on " + name + " has no key prefab; the key can never be made.", this);
        }
        ApplyIndicators();
    }

    public void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (KeyMade) return;   // done with; it is scenery from here

        KeyFragment held = interactor.GetCarried<KeyFragment>();
        if (held == null) return;   // nothing to fit: offer nothing, as the sell counter does

        int after = fitted.Count + 1;
        options.Add(new InteractionOption(fitKey, after >= Needed
            ? string.Format("Fit the last fragment  ({0}/{1}) - makes the key", after, Needed)
            : string.Format("Fit the fragment into the key maker  ({0}/{1})", after, Needed)));
    }

    public void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (key != fitKey || KeyMade || !HasAuthority) return;

        KeyFragment held = interactor.GetCarried<KeyFragment>();
        if (held == null) return;

        // Out of the hands the way a sale takes an item: never dropped, then fixed in place.
        interactor.ConsumeCarried();
        Seat(held);
        fitted.Add(held);

        ApplyIndicators();
        OnChanged();

        if (fitted.Count >= Needed) MakeKey();
    }

    /// <summary>Pin a fragment into its socket, so the progress is visible on the device itself.</summary>
    private void Seat(KeyFragment fragment)
    {
        Transform socket = sockets != null && fitted.Count < sockets.Length ? sockets[fitted.Count] : null;
        if (socket == null)
        {
            fragment.gameObject.SetActive(false);
            return;
        }

        fragment.OnDropped(socket.position, socket.rotation);
        fragment.transform.SetParent(socket, true);

        // It is part of the device now, not something to pick up again.
        foreach (Collider c in fragment.GetComponentsInChildren<Collider>()) c.enabled = false;
        fragment.enabled = false;
    }

    /// <summary>
    /// Consume the four fragments and lay the finished key on the table. The fragments are
    /// destroyed rather than hidden: they are gone into the key, and nothing should be able
    /// to find them again.
    /// </summary>
    private void MakeKey()
    {
        keyWasMade = true;

        for (int i = 0; i < fitted.Count; i++)
        {
            if (fitted[i] != null) Destroy(fitted[i].gameObject);
        }
        fitted.Clear();

        if (keyPrefab == null)
        {
            Debug.LogError("KeyMaker has no key prefab; the fragments were consumed and nothing was made.", this);
            return;
        }

        Transform at = output != null ? output : transform;
        GameObject go = Instantiate(keyPrefab, at.position, at.rotation);
        go.name = keyPrefab.name;

        madeKey = go.GetComponent<CompleteKey>();
        if (madeKey == null)
        {
            Debug.LogError("KeyMaker's key prefab has no CompleteKey.", this);
        }

        ApplyIndicators();
        OnChanged();
    }

    private void ApplyIndicators()
    {
        if (indicatorLights == null) return;
        for (int i = 0; i < indicatorLights.Length; i++)
        {
            if (indicatorLights[i] != null) indicatorLights[i].enabled = KeyMade || i < fitted.Count;
        }
    }

    void OnGUI()
    {
        // The one readout this level genuinely needs, and only while it means something:
        // nothing before the first fragment, and nothing once the key is in the world.
        if (fitted.Count == 0 && !KeyMade) return;

        if (KeyMade)
        {
            Hud.RowRight(4, "Key maker: the key is finished", new Color(1f, 0.86f, 0.5f));
            return;
        }

        Hud.RowRight(4, string.Format("Key maker: {0}/{1} fragments", fitted.Count, Needed),
                     new Color(0.85f, 0.92f, 1f));
    }
}
