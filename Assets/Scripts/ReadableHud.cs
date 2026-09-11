using UnityEngine;

/// <summary>
/// The player's view of whatever they are reading. Per-player and local, like the hands:
/// it only remembers which Readable is open and draws its words.
///
/// Reading never stops the game. Walk away and it closes on its own, so a note can never
/// be what is on screen when something comes round the corner.
/// </summary>
[DisallowMultipleComponent]
public class ReadableHud : MonoBehaviour
{
    [Tooltip("Walk further than this from what you are reading and it closes.")]
    public float closeDistance = 4.5f;

    private Readable current;

    private static readonly Color Paper = new Color(0.72f, 0.66f, 0.52f, 0.95f);
    private static readonly Color Ink = new Color(0.16f, 0.12f, 0.08f, 1f);
    private static readonly Color Edge = new Color(0.25f, 0.19f, 0.12f, 1f);

    /// <summary>What is open, or null.</summary>
    public Readable Current { get { return current; } }

    /// <summary>Open this, or close it if it is the one already open.</summary>
    public void Toggle(Readable readable)
    {
        current = current == readable ? null : readable;
    }

    void Update()
    {
        if (current == null) return;

        if (!current.isActiveAndEnabled ||
            (current.transform.position - transform.position).sqrMagnitude > closeDistance * closeDistance)
        {
            current = null;
        }
    }

    void OnGUI()
    {
        if (current == null) return;

        float line = Hud.LineHeight;
        float w = Mathf.Min(Screen.width - 40f, Screen.height * 0.8f);
        float pad = line * 0.8f;

        GUIContent body = new GUIContent(current.text);
        float textHeight = Hud.Paragraph.CalcHeight(body, w - pad * 2f);
        bool hasTitle = !string.IsNullOrEmpty(current.title);
        float h = pad * 2f + textHeight + (hasTitle ? line * 1.6f : 0f) + line;

        // Centred, but kept above the inventory bar's reserved strip.
        float maxBottom = Screen.height - Hud.BottomBarHeight - 8f;
        float y = Mathf.Min((Screen.height - h) * 0.5f, maxBottom - h);
        Rect paper = new Rect((Screen.width - w) * 0.5f, Mathf.Max(8f, y), w, h);

        Hud.Box(paper, Paper);
        Hud.Frame(paper, Edge, 3f);

        float cursor = paper.y + pad;
        if (hasTitle)
        {
            Hud.Ink(new Rect(paper.x, cursor, paper.width, line * 1.3f), current.title, Hud.Prompt, Ink);
            cursor += line * 1.6f;
        }

        Hud.Ink(new Rect(paper.x + pad, cursor, w - pad * 2f, textHeight), current.text, Hud.Paragraph, Ink);

        Hud.Ink(new Rect(paper.x, paper.yMax - line * 1.2f, paper.width - pad, line),
                "[" + current.readKey + "] close", Hud.ReadoutRight, Edge);
    }
}
