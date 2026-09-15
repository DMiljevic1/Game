using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads out what every monster is currently doing, and flashes the last noise the
/// player made. Purely a window on the simulation -- it decides nothing, so disabling
/// the component (or the whole GameObject) removes the readout and changes no
/// behaviour at all.
///
/// Claims left-hand HUD rows 5 and down.
/// </summary>
[DisallowMultipleComponent]
public class MonsterDebugHud : MonoBehaviour
{
    [Tooltip("Master switch. Off = nothing is drawn and nothing is subscribed.")]
    public bool show = true;

    [Tooltip("Most monsters listed before the rest are summarised as a count.")]
    public int maxListed = 6;

    [Tooltip("First left-hand HUD row to draw on. 0-4 are already claimed.")]
    public int firstRow = 5;

    [Tooltip("Seconds the last noise stays on screen.")]
    public float noiseFlashTime = 1.5f;

    private readonly List<Monster> monsters = new List<Monster>();
    private float lastRefresh;
    private NoiseEvent lastNoise;
    private float lastNoiseTime = -999f;

    void OnEnable()
    {
        Noise.OnNoiseEmitted += HandleNoise;
    }

    void OnDisable()
    {
        Noise.OnNoiseEmitted -= HandleNoise;
    }

    private void HandleNoise(NoiseEvent noise)
    {
        lastNoise = noise;
        lastNoiseTime = Time.time;
    }

    void OnGUI()
    {
        if (!show) return;

        // Monsters come and go with the night, so re-find them, but not every frame.
        if (Time.time - lastRefresh > 1f)
        {
            lastRefresh = Time.time;
            monsters.Clear();
            monsters.AddRange(Object.FindObjectsByType<Monster>(FindObjectsInactive.Exclude));
        }

        int row = firstRow;

        float age = Time.time - lastNoiseTime;
        if (age <= noiseFlashTime)
        {
            string source = lastNoise.source != null ? lastNoise.source.name : "world";
            Hud.Row(row++, string.Format("noise: {0}  r={1:0}m  @ {2}",
                                         source, lastNoise.radius, Format(lastNoise.position)),
                    new Color(1f, 1f, 1f, 1f - age / noiseFlashTime));
        }
        else
        {
            Hud.Row(row++, "noise: -", new Color(1f, 1f, 1f, 0.4f));
        }

        int listed = 0;
        for (int i = 0; i < monsters.Count; i++)
        {
            Monster m = monsters[i];
            if (m == null) continue;

            if (listed >= maxListed)
            {
                Hud.Row(row, string.Format("... and {0} more", monsters.Count - listed));
                return;
            }

            // What it is doing, plus the one thing you cannot see from the outside: whether
            // it has given up on the area or is still keeping its round near it.
            string extra = m.IsSearching ? string.Format("  {0} left to check", m.SearchesLeft)
                         : m.InterestTimeLeft > 0f ? string.Format("  interested {0:0}s", m.InterestTimeLeft)
                         : m.State == MonsterState.Patrol ? string.Format("  house in {0:0}s", m.ProwlTimeLeft)
                         : "";

            Hud.Row(row++, string.Format("{0}: {1}  agitation {2:0.0}{3}",
                                         m.name, m.State, m.Agitation, extra),
                    StateColor(m.State));
            listed++;
        }
    }

    private static string Format(Vector3 p)
    {
        return string.Format("({0:0}, {1:0})", p.x, p.z);
    }

    private static Color StateColor(MonsterState state)
    {
        switch (state)
        {
            case MonsterState.Chase: return new Color(1f, 0.35f, 0.3f);
            case MonsterState.Alerted: return new Color(1f, 0.7f, 0.25f);
            case MonsterState.Investigate: return new Color(1f, 0.95f, 0.45f);
            case MonsterState.Search: return new Color(0.85f, 1f, 0.5f);
            case MonsterState.Prowl: return new Color(0.8f, 0.55f, 1f);
            case MonsterState.Leaving: return new Color(0.6f, 1f, 0.85f);
            default: return new Color(0.65f, 0.85f, 1f);
        }
    }
}
