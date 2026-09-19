using UnityEngine;

/// <summary>
/// Takes the character away from a dead player and gives it back when they are revived.
/// A pure observer of <see cref="PlayerVitals"/>: it switches the listed scripts off from
/// the outside, the way the store does, so none of them has to know that death exists.
///
/// The avatar is hidden and made non-solid too, because what is left in the world is the
/// <see cref="PlayerBody"/>, not the player.
///
/// Co-op note: this is per-player and local. When there are teammates to watch, a
/// spectator camera goes here -- switched on by the same two events.
/// </summary>
[DisallowMultipleComponent]
public class PlayerDeathLock : MonoBehaviour
{
    [Tooltip("Whose death locks the controls. Found on this object if left empty.")]
    public PlayerVitals vitals;

    [Tooltip("Everything that lets a player act: movement, mouse look, the interactor, the " +
             "pack, the footstep emitter. Off while dead, back on when revived.")]
    public Behaviour[] controls;

    [Tooltip("The avatar's colliders, so a dead player is neither in the way nor found by a monster's touch.")]
    public Collider[] bodyColliders;

    [Tooltip("The avatar's visible parts. Hidden while dead: the body left in the world is the PlayerBody.")]
    public Renderer[] bodyRenderers;

    [Header("Look")]
    public Color overlay = new Color(0.05f, 0f, 0f, 0.55f);
    public Color titleColor = new Color(1f, 0.35f, 0.35f);

    private bool locked;

    public bool IsLocked { get { return locked; } }

    void Awake()
    {
        if (vitals == null) vitals = GetComponent<PlayerVitals>();
        if (vitals == null)
        {
            Debug.LogError("PlayerDeathLock on " + name + " has no PlayerVitals; a dead player will keep walking.", this);
        }
    }

    void OnEnable()
    {
        if (vitals == null) return;
        vitals.OnDied += Lock;
        vitals.OnRevived += Unlock;
    }

    void OnDisable()
    {
        if (vitals == null) return;
        vitals.OnDied -= Lock;
        vitals.OnRevived -= Unlock;
    }

    private void Lock()
    {
        locked = true;
        Apply(false);
    }

    private void Unlock()
    {
        locked = false;
        Apply(true);
    }

    void LateUpdate()
    {
        // Held off every frame, not just once: anything that restores what it suspended --
        // the store closing because its browser went away -- must not hand a corpse its legs back.
        if (locked) SetControls(false);
    }

    private void Apply(bool on)
    {
        SetControls(on);

        if (bodyColliders != null)
            for (int i = 0; i < bodyColliders.Length; i++)
                if (bodyColliders[i] != null) bodyColliders[i].enabled = on;

        if (bodyRenderers != null)
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null) bodyRenderers[i].enabled = on;
    }

    private void SetControls(bool on)
    {
        if (controls == null) return;
        for (int i = 0; i < controls.Length; i++)
            if (controls[i] != null && controls[i].enabled != on) controls[i].enabled = on;
    }

    void OnGUI()
    {
        if (!locked) return;

        // Behind every other readout, so the dimming never covers the status row or the
        // singleplayer countdown.
        GUI.depth = 1;

        Hud.Box(new Rect(0f, 0f, Screen.width, Screen.height), overlay);
        Hud.Label(new Rect(0f, Screen.height * 0.36f, Screen.width, Hud.Prompt.fontSize * 1.8f),
                  "YOU DIED", Hud.Prompt, titleColor);
        Hud.Label(new Rect(0f, Screen.height * 0.36f + Hud.Prompt.fontSize * 1.8f, Screen.width, Hud.LineHeight),
                  "Your body and everything you carried lie where you fell. A teammate can revive you here " +
                  "with adrenaline - slow and very loud - or carry you home and do it safely.",
                  Hud.Centered, new Color(1f, 1f, 1f, 0.8f));
    }
}
