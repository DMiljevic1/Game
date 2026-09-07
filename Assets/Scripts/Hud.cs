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
    private static int builtForHeight = -1;

    public static GUIStyle Readout { get { Build(); return readout; } }
    public static GUIStyle ReadoutRight { get { Build(); return readoutRight; } }
    public static GUIStyle Prompt { get { Build(); return prompt; } }
    public static GUIStyle Centered { get { Build(); return centered; } }

    /// <summary>Height of one readout line, for stacking rows down the corner.</summary>
    public static float LineHeight { get { Build(); return readout.fontSize * 1.5f; } }

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
