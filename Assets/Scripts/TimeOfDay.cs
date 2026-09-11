using UnityEngine;

/// <summary>
/// What time of day the level is, and the light that goes with it. Level 1 is always
/// night: there is no cycle, no clock and no phase change, so nothing here ticks. It puts
/// the moon and the night ambient up once, in Awake, and they stay.
///
/// NightDepth darkens that ambient with distance from the house. It reads the base colours
/// from here instead of scaling whatever is in RenderSettings, so it can never compound.
///
/// Co-op note: the night is fixed, so there is nothing to sync -- every client lights its
/// own scene from the same numbers.
/// </summary>
[DisallowMultipleComponent]
public class TimeOfDay : MonoBehaviour
{
    public static TimeOfDay Instance { get; private set; }

    [Header("Moonlight")]
    [Tooltip("The one directional light. It shines as the moon, so the canopy still throws shadows.")]
    public Light sun;
    public float nightIntensity = 0.12f;
    public Color nightColor = new Color(0.45f, 0.55f, 0.85f);
    [Tooltip("Compass heading the moon shines towards.")]
    public float moonYaw = 30f;
    [Tooltip("How high the moon rides, in degrees above the horizon.")]
    [Range(10f, 80f)] public float moonElevation = 40f;

    [Header("Ambient (gradient: sky above, ground below)")]
    public Color nightAmbientColor = new Color(0.30f, 0.38f, 0.60f);
    public float nightAmbient = 0.22f;

    // A strong equator is what lets trunks, walls and canopies read at night: they face
    // the horizon, and with near-black albedo a weak one leaves them as cut-outs.
    public Color AmbientSky { get { return nightAmbientColor * nightAmbient; } }
    public Color AmbientEquator { get { return AmbientSky * 0.7f; } }
    public Color AmbientGround { get { return AmbientSky * 0.3f; } }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second TimeOfDay exists on " + name + "; destroying it. There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (sun == null)
        {
            Debug.LogError("TimeOfDay has no sun assigned; the moonlight will not be set.", this);
        }

        ApplyLighting();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Puts the night's light on the scene. Runs once in Awake; the environment builder
    /// calls it to preview the night in the Scene view without Play mode.
    /// </summary>
    public void ApplyLighting()
    {
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(moonElevation, moonYaw, 0f);
            sun.intensity = nightIntensity;
            sun.color = nightColor;
        }

        // A gradient rather than the skybox: the night sky is near black, so skybox
        // ambient left nothing but the lamps. Colours also take effect immediately,
        // with no environment re-bake.
        if (RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight)
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
    }
}
