using UnityEngine;

/// <summary>
/// Turns a hit into something the player can feel: a red edge flash, a short camera
/// knock and an impact sound. It is a pure *observer* of <see cref="PlayerVitals"/> —
/// it subscribes to OnDamaged and never touches health, so deleting it costs the
/// feedback and never the damage.
///
/// No coroutines: the flash is one float ticked down in Update, so any number of hits
/// in quick succession can only ever retrigger the same effect rather than stacking
/// several of them on top of each other.
/// </summary>
[DisallowMultipleComponent]
public class DamageFeedback : MonoBehaviour
{
    [Header("Wiring (found automatically if left empty)")]
    public PlayerVitals vitals;
    public CameraShake cameraShake;
    public AudioSource audioSource;

    [Header("Red flash")]
    [Tooltip("Peak opacity of the flash. 1 would be opaque red; keep it low so the world stays visible.")]
    [Range(0f, 1f)] public float flashStrength = 0.45f;
    [Tooltip("Seconds the flash takes to fade away.")]
    public float flashDuration = 0.55f;
    public Color flashColor = new Color(0.65f, 0.03f, 0.03f, 1f);
    [Tooltip("0 = flat wash over the whole screen, 1 = only the very edges. A vignette reads as a hit; a full red screen reads as a bug.")]
    [Range(0f, 1f)] public float flashEdgeBias = 0.55f;

    [Header("Camera knock")]
    public bool shakeCamera = true;
    [Tooltip("Metres of sway at full strength.")]
    public float shakeAmplitude = 0.07f;
    [Tooltip("Seconds the knock lasts.")]
    public float shakeDuration = 0.28f;

    [Header("Sound")]
    [Tooltip("Assign a short impact/grunt clip. Left empty, the effect is silent — deliberately, rather than making do with a bad placeholder.")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 0.8f;
    [Tooltip("Random pitch spread, so repeated hits do not machine-gun the same sample.")]
    [Range(0f, 0.5f)] public float pitchVariation = 0.12f;

    [Header("Scaling")]
    [Tooltip("Damage that produces the full effect. Smaller hits are proportionally softer; bigger ones are clamped here.")]
    public float damageForFullEffect = 25f;
    [Tooltip("Weakest a hit is allowed to feel, however small it was.")]
    [Range(0f, 1f)] public float minimumIntensity = 0.5f;

    /// <summary>Current opacity of the flash, 0 when it is over. Read-only: a future
    /// full-screen shader could drive itself from this instead of the IMGUI overlay.</summary>
    public float FlashAlpha { get { return CurrentFlashAlpha(); } }

    private float flashTimeLeft;
    private float flashPeak;          // the alpha this flash started at
    private Texture2D vignette;
    private float vignetteBuiltForBias = -1f;

    void Awake()
    {
        if (vitals == null) vitals = GetComponent<PlayerVitals>();
        if (vitals == null) vitals = GetComponentInParent<PlayerVitals>();
        if (vitals == null)
        {
            Debug.LogError("DamageFeedback on " + name + " found no PlayerVitals; the player will take damage with no feedback at all.", this);
            return;
        }

        if (cameraShake == null)
        {
            Camera view = GetComponentInChildren<Camera>();
            if (view != null) cameraShake = view.GetComponent<CameraShake>();
        }

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;   // it happens to you, not somewhere in the world
        }
    }

    void OnEnable()
    {
        if (vitals != null) vitals.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        if (vitals != null) vitals.OnDamaged -= HandleDamaged;
        flashTimeLeft = 0f;
    }

    void Update()
    {
        if (flashTimeLeft > 0f) flashTimeLeft = Mathf.Max(0f, flashTimeLeft - Time.deltaTime);
    }

    private void HandleDamaged()
    {
        float intensity = Mathf.Lerp(minimumIntensity, 1f, Intensity01(vitals.LastDamageAmount));
        Play(intensity);
    }

    /// <summary>
    /// Runs the whole effect at <paramref name="intensity"/> (0-1). Public so a future
    /// source of pain — a fall, a trap — can reuse it without going through PlayerVitals.
    /// </summary>
    public void Play(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);

        // Retrigger rather than layer: a second hit mid-fade restarts at the brighter of
        // the two, so rapid hits stay bright but never wash out to solid red.
        flashPeak = Mathf.Max(CurrentFlashAlpha(), flashStrength * intensity);
        flashTimeLeft = flashDuration;

        if (shakeCamera && cameraShake != null)
        {
            cameraShake.Shake(shakeDuration, shakeAmplitude * intensity);
        }

        if (hitSound != null && audioSource != null)
        {
            audioSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            audioSource.PlayOneShot(hitSound, hitVolume * Mathf.Lerp(0.7f, 1f, intensity));
        }
    }

    private float Intensity01(float damage)
    {
        if (damageForFullEffect <= 0f) return 1f;
        return Mathf.Clamp01(damage / damageForFullEffect);
    }

    private float CurrentFlashAlpha()
    {
        if (flashTimeLeft <= 0f || flashDuration <= 0f) return 0f;
        float k = flashTimeLeft / flashDuration;
        return flashPeak * k * k;   // fades fast at first, then lingers faintly
    }

    void OnDestroy()
    {
        if (vignette != null) Destroy(vignette);
    }

    void OnGUI()
    {
        float alpha = CurrentFlashAlpha();
        if (alpha <= 0.001f) return;

        Color previous = GUI.color;
        GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Vignette(), ScaleMode.StretchToFill);
        GUI.color = previous;
    }

    /// <summary>
    /// A soft radial gradient, transparent in the middle and red at the edges. Built once
    /// and stretched, so it costs one small texture no matter the resolution.
    /// </summary>
    private Texture2D Vignette()
    {
        if (vignette != null && Mathf.Approximately(vignetteBuiltForBias, flashEdgeBias)) return vignette;
        if (vignette != null) Destroy(vignette);
        vignetteBuiltForBias = flashEdgeBias;

        const int size = 64;
        vignette = new Texture2D(size, size, TextureFormat.ARGB32, false);
        vignette.wrapMode = TextureWrapMode.Clamp;
        vignette.filterMode = FilterMode.Bilinear;
        vignette.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                // Distance to the nearest edge, squared off a little so the corners are
                // not the only thing that glows on a wide monitor.
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx * 0.85f + dy * dy));
                // How much of the flash survives in the middle of the screen: at bias 1 the
                // centre stays completely clear, at 0 it is nearly a flat wash.
                float centre = (1f - flashEdgeBias) * 0.35f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Lerp(centre, 1f, Mathf.SmoothStep(0f, 1f, r)));
            }
        }

        vignette.SetPixels(pixels);
        vignette.Apply();
        return vignette;
    }
}
