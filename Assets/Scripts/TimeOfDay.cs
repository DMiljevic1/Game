using UnityEngine;

public enum DayPhase
{
    Day,
    Dusk,
    Night,
    Dawn
}

/// <summary>
/// The single authoritative clock for a level. Every other system (generator drain,
/// monster spawning, ambience) reads this rather than keeping its own timer.
///
/// Co-op note: only the authority advances the clock. When netcode is added,
/// cycleTime becomes a synced variable and HasAuthority becomes IsServer -- nothing
/// else in this class needs to change, and no other system should ever write time.
/// </summary>
[DisallowMultipleComponent]
public class TimeOfDay : MonoBehaviour
{
    public static TimeOfDay Instance { get; private set; }

    [Header("Phase lengths (real seconds)")]
    public float dayLength = 480f;    // 8:00 - explore, gather, prepare
    public float duskLength = 120f;   // 2:00 - the warning window: get home
    public float nightLength = 300f;  // 5:00 - survive
    public float dawnLength = 60f;    // 1:00 - relief before full daylight

    [Header("Flow")]
    [Tooltip("Multiplier for testing. 1 = real pace, 8 = a full cycle in two minutes.")]
    public float timeScale = 1f;
    public bool paused = false;
    [Tooltip("Where the very first cycle starts, 0-1 through the day phase.")]
    [Range(0f, 1f)] public float startOfDayOffset = 0f;

    [Header("Lighting")]
    public Light sun;
    public float sunYaw = 30f;
    public float dayIntensity = 1.2f;
    public float nightIntensity = 0.12f;
    public Color dayColor = new Color(1.00f, 0.96f, 0.88f);
    public Color horizonColor = new Color(1.00f, 0.55f, 0.28f);
    public Color nightColor = new Color(0.45f, 0.55f, 0.85f);
    public float dayAmbient = 1.0f;
    public float nightAmbient = 0.22f;

    [Header("Debug")]
    public bool showDebugClock = true;

    // Subscribers use these to react to the world changing state. Initialised so
    // callers never have to null check.
    public event System.Action<DayPhase, DayPhase> OnPhaseChanged = delegate { };
    public event System.Action<int> OnDayStarted = delegate { };

    private float cycleTime;          // seconds elapsed within the current 24h cycle
    private DayPhase phase = DayPhase.Day;
    private int dayNumber = 1;

    public DayPhase Phase { get { return phase; } }
    public int DayNumber { get { return dayNumber; } }

    /// <summary>True only during full night.</summary>
    public bool IsNight { get { return phase == DayPhase.Night; } }

    /// <summary>Dusk and night: when it is unsafe to be outside.</summary>
    public bool IsDark { get { return phase == DayPhase.Dusk || phase == DayPhase.Night; } }

    public float CycleLength { get { return dayLength + duskLength + nightLength + dawnLength; } }

    /// <summary>Seconds of real time until the current phase ends.</summary>
    public float TimeUntilPhaseEnd
    {
        get
        {
            float end = PhaseEnd(phase);
            return Mathf.Max(0f, (end - cycleTime) / Mathf.Max(0.0001f, timeScale));
        }
    }

    /// <summary>In-game hour, 0-24, for display and for driving the sun.</summary>
    public float HourOfDay
    {
        get
        {
            // Each phase covers a fixed slice of the in-game clock, so a short night
            // still moves the sun through the full night-time arc.
            // Sunrise (06:00) and sunset (18:00) sit in the MIDDLE of dawn and dusk,
            // so those phases are the light actually changing rather than already-dark.
            if (cycleTime < PhaseEnd(DayPhase.Day))
                return Mathf.Lerp(7f, 17f, Progress(0f, dayLength));
            if (cycleTime < PhaseEnd(DayPhase.Dusk))
                return Mathf.Lerp(17f, 19f, Progress(PhaseEnd(DayPhase.Day), duskLength));
            if (cycleTime < PhaseEnd(DayPhase.Night))
                return Mathf.Repeat(Mathf.Lerp(19f, 29f, Progress(PhaseEnd(DayPhase.Dusk), nightLength)), 24f);
            return Mathf.Lerp(5f, 7f, Progress(PhaseEnd(DayPhase.Night), dawnLength));
        }
    }

    // Authority seam. Becomes IsServer/IsHost once netcode is in; until then the
    // local game is the authority.
    protected virtual bool HasAuthority { get { return true; } }

    /// <summary>
    /// Overwrite the clock position and refresh everything derived from it. Normal
    /// play never calls this: it exists so a networked client can mirror the
    /// authority's value, and so the editor can preview a phase without Play mode.
    /// </summary>
    public void SetCycleTime(float seconds)
    {
        cycleTime = Mathf.Repeat(seconds, CycleLength);

        DayPhase current = PhaseAt(cycleTime);
        if (current != phase)
        {
            DayPhase previous = phase;
            phase = current;
            OnPhaseChanged(previous, current);
        }

        ApplyLighting();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second TimeOfDay exists on " + name + "; destroying it. There must be exactly one clock.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (sun == null)
        {
            Debug.LogError("TimeOfDay has no sun assigned; lighting will not change with time.", this);
        }

        cycleTime = dayLength * startOfDayOffset;
        phase = PhaseAt(cycleTime);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        ApplyLighting();
        OnDayStarted(dayNumber);
    }

    void Update()
    {
        if (HasAuthority && !paused)
        {
            cycleTime += Time.deltaTime * timeScale;

            if (cycleTime >= CycleLength)
            {
                cycleTime -= CycleLength;
                dayNumber++;
                OnDayStarted(dayNumber);
            }
        }

        DayPhase current = PhaseAt(cycleTime);
        if (current != phase)
        {
            DayPhase previous = phase;
            phase = current;
            OnPhaseChanged(previous, current);
        }

        ApplyLighting();
    }

    private float Progress(float phaseStart, float length)
    {
        return Mathf.Clamp01((cycleTime - phaseStart) / Mathf.Max(0.0001f, length));
    }

    private float PhaseEnd(DayPhase p)
    {
        switch (p)
        {
            case DayPhase.Day: return dayLength;
            case DayPhase.Dusk: return dayLength + duskLength;
            case DayPhase.Night: return dayLength + duskLength + nightLength;
            default: return CycleLength;
        }
    }

    private DayPhase PhaseAt(float t)
    {
        if (t < PhaseEnd(DayPhase.Day)) return DayPhase.Day;
        if (t < PhaseEnd(DayPhase.Dusk)) return DayPhase.Dusk;
        if (t < PhaseEnd(DayPhase.Night)) return DayPhase.Night;
        return DayPhase.Dawn;
    }

    private void ApplyLighting()
    {
        if (sun == null) return;

        float hour = HourOfDay;

        // 06:00 puts the sun on the horizon, 12:00 overhead.
        float pitch = (hour / 24f) * 360f - 90f;
        sun.transform.rotation = Quaternion.Euler(pitch, sunYaw, 0f);

        // How high the sun sits, -1 (deep night) to 1 (noon).
        float elevation = Mathf.Sin(pitch * Mathf.Deg2Rad);

        float daylight = Mathf.Clamp01(elevation / 0.25f);          // full brightness once well up
        float horizon = Mathf.Clamp01(1f - Mathf.Abs(elevation) / 0.25f); // peaks at sunrise/sunset

        // Ambient lags the sun: the sky stays lit for a while after it drops below
        // the horizon, which is what makes twilight readable instead of a light switch.
        float skylight = Mathf.Clamp01((elevation + 0.18f) / 0.45f);

        sun.intensity = Mathf.Lerp(nightIntensity, dayIntensity, daylight);

        Color c = Color.Lerp(nightColor, dayColor, daylight);
        sun.color = Color.Lerp(c, horizonColor, horizon * 0.8f);

        RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbient, dayAmbient, skylight);
    }

    void OnGUI()
    {
        if (!showDebugClock) return;

        int h = Mathf.FloorToInt(HourOfDay);
        int m = Mathf.FloorToInt((HourOfDay - h) * 60f);

        string line = string.Format("Day {0}   {1:00}:{2:00}   {3}   next in {4:0}s",
                                    dayNumber, h, m, phase.ToString().ToUpper(), TimeUntilPhaseEnd);

        GUI.Label(new Rect(12f, 10f, 420f, 22f), line);
        if (timeScale != 1f || paused)
        {
            GUI.Label(new Rect(12f, 30f, 420f, 22f),
                      paused ? "clock PAUSED" : string.Format("clock x{0}", timeScale));
        }
    }
}
