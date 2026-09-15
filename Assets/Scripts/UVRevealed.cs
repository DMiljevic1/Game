using UnityEngine;

/// <summary>
/// Something painted in ultraviolet: invisible under moonlight or an ordinary flashlight, and
/// visible only while a lit <see cref="UVFlashlight"/> beam is actually on it.
///
/// It is a **pure observer of the light** -- it switches renderers and nothing else. It holds
/// no objective state, has no idea what it is part of, and deleting it from an object would
/// cost that object its invisibility and never its behaviour. That is what lets the same
/// component serve the footprints, the circle on the ground, and whatever else turns out to
/// be worth hiding later.
///
/// Co-op note: purely visual and per-viewer, like NightDepth. Each client reveals its own
/// marks with its own flashlight; there is nothing here to replicate.
/// </summary>
[DisallowMultipleComponent]
public class UVRevealed : MonoBehaviour
{
    [Tooltip("Renderers to hide. Found in this object's children if left empty.")]
    public Renderer[] hidden;

    [Tooltip("The point tested against the beam. This object's own transform if left empty -- " +
             "set it for something large, so the beam does not have to touch the pivot.")]
    public Transform probe;

    [Tooltip("Once revealed, stay visible this long after the beam leaves, so a mark does not " +
             "flicker out the instant the player's aim drifts off it.")]
    public float linger = 0.35f;

    private float litUntil = -1f;

    /// <summary>True while this is actually being shown.</summary>
    public bool IsVisible { get { return Time.time <= litUntil; } }

    void Awake()
    {
        if (hidden == null || hidden.Length == 0) hidden = GetComponentsInChildren<Renderer>(true);
        if (probe == null) probe = transform;

        Show(false);
    }

    void LateUpdate()
    {
        // The whole level's worth of marks runs this, so the no-flashlight case has to be one
        // static check and out. AnyLit is that check.
        if (UVFlashlight.AnyLit && UVFlashlight.Illuminates(probe.position))
        {
            litUntil = Time.time + Mathf.Max(0f, linger);
        }

        Show(IsVisible);
    }

    private void Show(bool on)
    {
        if (hidden == null) return;
        for (int i = 0; i < hidden.Length; i++)
        {
            if (hidden[i] != null && hidden[i].enabled != on) hidden[i].enabled = on;
        }
    }
}
