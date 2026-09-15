using UnityEngine;

/// <summary>
/// What one pour of <see cref="MagicPowder"/> leaves on the ground.
///
/// It is **pure presentation** -- a patch of dust and one line of text. It reveals nothing
/// and decides nothing; the pour has already done all of that by the time this exists. So
/// deleting it would cost the player the feedback and never the mechanic.
///
/// The patches are left lying where they fall on purpose. A player sweeping a hillside needs
/// to see where they have already looked, and dust on the ground says that better than any
/// counter could.
/// </summary>
[DisallowMultipleComponent]
public class PowderPatch : MonoBehaviour
{
    [Tooltip("Seconds the result line stays on screen. The dust itself stays for good.")]
    public float messageSeconds = 2.6f;

    private string message = "";
    private Color tint = Color.white;
    private float shownAt = -1f;

    /// <summary>Say what this pour turned up. Called once, by the powder, as it lands.</summary>
    public void Report(string text, bool foundSomething)
    {
        message = text;
        tint = foundSomething ? new Color(1f, 0.86f, 0.5f) : new Color(0.78f, 0.78f, 0.84f);
        shownAt = Time.time;
    }

    void OnGUI()
    {
        if (shownAt < 0f || Time.time - shownAt > messageSeconds) return;

        // Under the crosshair and the interaction prompt, so it never sits on top of either.
        float y = Screen.height * 0.5f + Hud.Prompt.fontSize * 3.4f;
        Hud.Label(new Rect(0f, y, Screen.width, Hud.LineHeight * 1.4f), message, Hud.Centered, tint);
    }
}
