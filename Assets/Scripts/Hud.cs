using UnityEngine;

/// <summary>
/// Shared styles for the on-screen text. Everything is drawn white on a dark drop
/// shadow so it stays readable against both a black night field and a bright sky.
/// Sizes scale with screen height so text does not shrink on a 4K monitor.
///
/// This is placeholder HUD: real UI comes later, but unreadable text is not a
/// "make it pretty later" problem, it is a "cannot test the game" problem.
/// </summary>
public static class Hud
{
    private static GUIStyle readout;
    private static GUIStyle readoutRight;
    private static GUIStyle prompt;
    private static GUIStyle centered;
    private static GUIStyle slotLabel;
    private static GUIStyle slotKey;
    private static GUIStyle button;
    private static GUIStyle paragraph;
    private static Texture2D pixel;
    private static int builtForHeight = -1;

    public static GUIStyle Readout { get { Build(); return readout; } }
    public static GUIStyle ReadoutRight { get { Build(); return readoutRight; } }
    public static GUIStyle Prompt { get { Build(); return prompt; } }
    public static GUIStyle Centered { get { Build(); return centered; } }

    /// <summary>Small centred text that fits inside an inventory slot.</summary>
    public static GUIStyle SlotLabel { get { Build(); return slotLabel; } }

    /// <summary>The key badge in the corner of an inventory slot.</summary>
    public static GUIStyle SlotKey { get { Build(); return slotKey; } }

    /// <summary>A clickable button, scaled like the rest of the HUD. The only place in
    /// the game the mouse is used, so it needs to be the size of a real target.</summary>
    public static GUIStyle Button { get { Build(); return button; } }

    /// <summary>Word-wrapped body text for notes and papers, read at leisure rather than at a glance.</summary>
    public static GUIStyle Paragraph { get { Build(); return paragraph; } }

    /// <summary>Height of one readout line, for stacking rows down the corner.</summary>
    public static float LineHeight { get { Build(); return readout.fontSize * 1.5f; } }

    /// <summary>
    /// Height reserved along the bottom of the screen for the inventory bar. One number,
    /// one owner: the bar draws inside it and everything else keeps clear above it, so the
    /// two cannot drift apart and overlap.
    /// </summary>
    public static float BottomBarHeight { get { return Mathf.Round(Screen.height * 0.095f); } }

    private static void Build()
    {
        if (readout != null && builtForHeight == Screen.height) return;
        builtForHeight = Screen.height;

        int readoutSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.022f)); // ~24px at 1080p
        int promptSize = Mathf.Max(20, Mathf.RoundToInt(Screen.height * 0.030f)); // ~32px at 1080p

        readout = new GUIStyle(GUI.skin.label);
        readout.fontSize = readoutSize;
        readout.fontStyle = FontStyle.Bold;
        readout.alignment = TextAnchor.MiddleLeft;
        readout.normal.textColor = Color.white;

        readoutRight = new GUIStyle(readout);
        readoutRight.alignment = TextAnchor.MiddleRight;

        prompt = new GUIStyle(GUI.skin.label);
        prompt.fontSize = promptSize;
        prompt.fontStyle = FontStyle.Bold;
        prompt.alignment = TextAnchor.MiddleCenter;
        prompt.normal.textColor = Color.white;

        centered = new GUIStyle(prompt);

        slotLabel = new GUIStyle(GUI.skin.label);
        slotLabel.fontSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height * 0.0155f)); // ~17px at 1080p
        slotLabel.fontStyle = FontStyle.Bold;
        slotLabel.alignment = TextAnchor.MiddleCenter;
        slotLabel.wordWrap = true;
        slotLabel.normal.textColor = Color.white;

        slotKey = new GUIStyle(slotLabel);
        slotKey.alignment = TextAnchor.UpperLeft;
        slotKey.wordWrap = false;

        button = new GUIStyle(GUI.skin.button);
        button.fontSize = readoutSize;
        button.fontStyle = FontStyle.Bold;

        paragraph = new GUIStyle(readout);
        paragraph.fontStyle = FontStyle.Normal;
        paragraph.alignment = TextAnchor.UpperLeft;
        paragraph.wordWrap = true;
    }

    /// <summary>
    /// A flat filled rectangle. One shared 1x1 texture, built once and rebuilt only if the
    /// graphics device throws it away -- so drawing a panel costs no allocation per frame.
    /// </summary>
    public static void Box(Rect rect, Color color)
    {
        if (pixel == null)
        {
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
            pixel.hideFlags = HideFlags.HideAndDontSave;
        }

        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }

    /// <summary>An outlined rectangle, drawn as four thin boxes.</summary>
    public static void Frame(Rect rect, Color color, float thickness)
    {
        Box(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Box(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Box(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Box(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    /// <summary>Draw text with a shadow behind it. Tint applies to the text only.</summary>
    public static void Label(Rect rect, string text, GUIStyle style, Color tint)
    {
        Color previous = GUI.color;

        // Shadow follows the text's alpha, so anything that fades out fades away
        // completely instead of leaving its shadow behind.
        GUI.color = new Color(0f, 0f, 0f, 0.85f * tint.a);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);

        GUI.color = tint;
        GUI.Label(rect, text, style);

        GUI.color = previous;
    }

    public static void Label(Rect rect, string text, GUIStyle style)
    {
        Label(rect, text, style, Color.white);
    }

    /// <summary>
    /// Text with no shadow, for dark ink on a light panel -- a note, a map -- where the
    /// black shadow <see cref="Label"/> draws would only smear it.
    /// </summary>
    public static void Ink(Rect rect, string text, GUIStyle style, Color ink)
    {
        Color previous = GUI.color;
        GUI.color = ink;
        GUI.Label(rect, text, style);
        GUI.color = previous;
    }

    /// <summary>A readout row in the top-left corner. Row 0 is the top line.</summary>
    public static void Row(int row, string text)
    {
        Row(row, text, Color.white);
    }

    public static void Row(int row, string text, Color tint)
    {
        Build();
        Label(new Rect(14f, 10f + row * LineHeight, 700f, LineHeight), text, readout, tint);
    }

    /// <summary>
    /// A readout row in the top-RIGHT corner. Row 0 is the top line. Right-hand rows
    /// are numbered separately from <see cref="Row"/>, so the two corners cannot collide.
    /// </summary>
    public static void RowRight(int row, string text)
    {
        RowRight(row, text, Color.white);
    }

    public static void RowRight(int row, string text, Color tint)
    {
        Build();
        Label(new Rect(Screen.width - 714f, 10f + row * LineHeight, 700f, LineHeight), text, readoutRight, tint);
    }

    /// <summary>The interaction prompt, centred just below the crosshair.</summary>
    public static void CentrePrompt(string text)
    {
        Build();
        Label(new Rect(0f, Screen.height * 0.5f + prompt.fontSize * 0.9f, Screen.width, prompt.fontSize * 1.6f),
              text, prompt);
    }
}
