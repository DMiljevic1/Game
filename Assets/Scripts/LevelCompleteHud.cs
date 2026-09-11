using UnityEngine;

/// <summary>
/// Shows Tom's map when the case is opened, then "Level complete". It only reads the
/// Expedition and its event -- deleting it costs the screen, never the ending.
///
/// The map is deliberately vague: this house circled, a route out through the trees, and
/// a second mark. Where "2" is gets decided with Level 2.
/// </summary>
[DisallowMultipleComponent]
public class LevelCompleteHud : MonoBehaviour
{
    [Tooltip("The level objective. Falls back to Expedition.Instance if left empty.")]
    public Expedition expedition;

    [Header("Text")]
    public string title = "TOM'S MAP";
    public string note = "Gone ahead.   - R & N";
    public string banner = "LEVEL COMPLETE";

    [Tooltip("Seconds the whole map stays up before it shrinks to one corner line.")]
    public float fullSeconds = 14f;

    private float shownAt = -1f;

    private static readonly Color Paper = new Color(0.72f, 0.66f, 0.52f, 0.95f);
    private static readonly Color Ink = new Color(0.22f, 0.16f, 0.10f, 1f);
    private static readonly Color Pencil = new Color(0.36f, 0.32f, 0.28f, 1f);
    private static readonly Color Gold = new Color(1f, 0.86f, 0.5f, 1f);

    void Start()
    {
        if (expedition == null) expedition = Expedition.Instance;
        if (expedition == null)
        {
            Debug.LogError("LevelCompleteHud on " + name + " has no Expedition to watch.", this);
            return;
        }
        expedition.OnLevelComplete += Show;
    }

    void OnDestroy()
    {
        if (expedition != null) expedition.OnLevelComplete -= Show;
    }

    private void Show()
    {
        shownAt = Time.time;
    }

    void OnGUI()
    {
        if (shownAt < 0f) return;

        // After a while the map gets out of the way; the level carries on underneath.
        if (Time.time - shownAt > fullSeconds)
        {
            Hud.RowRight(2, banner + " - Tom's map found", Gold);
            return;
        }

        float h = Screen.height;
        float w = Mathf.Min(Screen.width - 40f, h * 0.95f);
        Rect paper = new Rect((Screen.width - w) * 0.5f, h * 0.12f, w, h * 0.6f);

        Hud.Box(paper, Paper);
        Hud.Frame(paper, Ink, 3f);

        float line = Hud.LineHeight;
        Hud.Ink(new Rect(paper.x, paper.y + line * 0.4f, paper.width, line * 1.4f), title, Hud.Prompt, Ink);

        // The sketch: "1" here, a dotted route through the trees, "2" somewhere beyond.
        Rect sketch = new Rect(paper.x + w * 0.08f, paper.y + line * 2.2f, w * 0.84f, paper.height - line * 4.6f);
        Vector2 home = new Vector2(sketch.x + sketch.width * 0.16f, sketch.yMax - sketch.height * 0.2f);
        Vector2 next = new Vector2(sketch.x + sketch.width * 0.84f, sketch.y + sketch.height * 0.2f);

        Mark(home, "1", line);
        Mark(next, "2", line);

        const int dots = 22;
        for (int i = 1; i < dots; i++)
        {
            float t = i / (float)dots;
            // Bowed, so it reads as a path through country rather than a ruler line.
            Vector2 p = Vector2.Lerp(home, next, t) + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * sketch.height * 0.18f);
            Hud.Box(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), Ink);
        }

        // A band of trees between the two, drawn as rough ink ticks.
        for (int i = 0; i < 9; i++)
        {
            float x = sketch.x + sketch.width * (0.3f + i * 0.045f);
            float y = sketch.y + sketch.height * (0.35f + (i % 3) * 0.1f);
            Hud.Ink(new Rect(x, y, line, line), "^", Hud.Readout, Pencil);
        }

        Hud.Ink(new Rect(paper.x, paper.yMax - line * 1.9f, paper.width - w * 0.08f, line), note, Hud.ReadoutRight, Pencil);

        Hud.Label(new Rect(0f, paper.yMax + line * 0.6f, Screen.width, line * 1.6f), banner, Hud.Prompt, Gold);
    }

    private static void Mark(Vector2 at, string label, float size)
    {
        Rect r = new Rect(at.x - size * 0.7f, at.y - size * 0.7f, size * 1.4f, size * 1.4f);
        Hud.Frame(r, Ink, 3f);
        Hud.Ink(r, label, Hud.Centered, Ink);
    }
}
