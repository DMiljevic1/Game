using UnityEngine;

/// <summary>
/// The mark on the ground at the end of the UV trail: the place where <see cref="MagicPowder"/>
/// brings the <see cref="Level2Door"/> into the world.
///
/// **It is found, not used.** There is deliberately no prompt and no key here -- you cannot
/// aim at something that is not there, and a circle that announced itself would give the game
/// away to anyone who wandered past without a UV flashlight. Instead the powder is poured
/// wherever the player is looking and asks whatever is nearby whether it wants revealing.
/// That keeps the search honest: the player finds this by sweeping, not by being prompted.
///
/// It owns one irreversible fact -- whether the powder has been scattered here -- and hands
/// the actual reveal to the door.
///
/// Co-op note: authority-owned, like a sale. Clients see the dusting and the door appear.
/// </summary>
[DisallowMultipleComponent]
public class RevealCircle : MonoBehaviour, IPowderRevealable
{
    [Tooltip("What appears when the powder is scattered here.")]
    public Level2Door door;

    [Tooltip("Left on the ground once the powder has been used, so the place stays readable " +
             "afterwards without a UV flashlight. Optional.")]
    public Renderer[] dusting;

    private bool used;

    /// <summary>True once the powder has been scattered here. One way: a door found stays found.</summary>
    public bool HasBeenRevealed { get { return used; } }

    void Awake()
    {
        if (GetComponentInChildren<Collider>() == null)
        {
            Debug.LogError("RevealCircle on " + name + " has no collider, so the powder can never find it.", this);
        }
        ShowDusting(false);
    }

    void Start()
    {
        if (door == null)
        {
            Debug.LogError("RevealCircle on " + name + " has no Level2Door; the powder would do nothing.", this);
        }
    }

    /// <summary>
    /// The powder has been scattered within reach of this. Only <see cref="MagicPowder"/>
    /// calls it, and only once -- the second pour on the same spot does nothing.
    /// </summary>
    public void Reveal()
    {
        if (used || door == null) return;

        used = true;
        ShowDusting(true);
        door.Reveal();
    }

    private void ShowDusting(bool on)
    {
        if (dusting == null) return;
        for (int i = 0; i < dusting.Length; i++)
            if (dusting[i] != null) dusting[i].enabled = on;
    }
}
