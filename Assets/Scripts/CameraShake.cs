using UnityEngine;

/// <summary>
/// A short, damped shake of the view. Lives on the camera and is driven by whatever
/// caused it (<see cref="DamageFeedback"/> today), so anything else that wants a knock
/// can call <see cref="Shake"/> without owning a copy of the maths.
///
/// It only ever offsets the camera's LOCAL position and rolls it around its own forward
/// axis, so it cannot rotate the aim: <c>forward</c> is unchanged and the interaction ray,
/// the flashlight beam and player movement all keep pointing exactly where they did.
/// MouseLook rewrites localRotation every Update, so the roll is re-applied in LateUpdate
/// and the previous frame's roll is removed first — it can never accumulate, even if
/// MouseLook is switched off.
/// </summary>
[DisallowMultipleComponent]
public class CameraShake : MonoBehaviour
{
    [Tooltip("Metres of sideways/vertical sway at full strength. Keep it small: this is a flinch, not an earthquake.")]
    public float defaultAmplitude = 0.06f;
    [Tooltip("Seconds a default shake lasts.")]
    public float defaultDuration = 0.25f;
    [Tooltip("Degrees of roll at full strength, on top of the positional sway.")]
    public float rollDegrees = 1.5f;
    [Tooltip("Wobbles per second. Higher is a sharper rattle, lower is a lurch.")]
    public float frequency = 22f;

    private Vector3 basePosition;
    private Quaternion appliedRoll = Quaternion.identity;

    private float duration;
    private float timeLeft;
    private float amplitude;
    private float seed;

    public bool IsShaking { get { return timeLeft > 0f; } }

    void Awake()
    {
        basePosition = transform.localPosition;
        seed = Random.value * 100f;
    }

    void OnDisable()
    {
        // Never leave the camera parked off-centre or tilted.
        timeLeft = 0f;
        transform.localPosition = basePosition;
        transform.localRotation = transform.localRotation * Quaternion.Inverse(appliedRoll);
        appliedRoll = Quaternion.identity;
    }

    /// <summary>Shake with the inspector defaults.</summary>
    public void Shake()
    {
        Shake(defaultDuration, defaultAmplitude);
    }

    /// <summary>
    /// Start a shake. Retriggering keeps whichever is stronger and restarts the timer,
    /// so a burst of hits reads as one sustained rattle rather than stacking into a mess.
    /// </summary>
    public void Shake(float shakeDuration, float shakeAmplitude)
    {
        if (shakeDuration <= 0f || shakeAmplitude <= 0f) return;

        amplitude = Mathf.Max(amplitude * Damping(), shakeAmplitude);
        duration = Mathf.Max(shakeDuration, timeLeft);
        timeLeft = duration;
        seed = Random.value * 100f;
    }

    void LateUpdate()
    {
        // Take last frame's roll back off before reading the look rotation, so what we
        // add is always relative to where MouseLook actually wants to be pointing.
        Quaternion look = transform.localRotation * Quaternion.Inverse(appliedRoll);

        if (timeLeft <= 0f)
        {
            if (appliedRoll != Quaternion.identity)
            {
                transform.localPosition = basePosition;
                transform.localRotation = look;
                appliedRoll = Quaternion.identity;
            }
            return;
        }

        timeLeft = Mathf.Max(0f, timeLeft - Time.deltaTime);

        float strength = amplitude * Damping();
        float t = Time.time * frequency;

        // Perlin rather than Random so the sway is continuous — white noise jitters
        // like a broken cable instead of a knock.
        float x = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(seed + 17f, t) - 0.5f) * 2f;
        float z = (Mathf.PerlinNoise(seed + 41f, t) - 0.5f) * 2f;

        // Roll is expressed relative to the default amplitude, so turning the sway up
        // tilts the view further too and the two stay in proportion.
        float rollScale = defaultAmplitude <= 0.0001f ? 1f : strength / defaultAmplitude;
        appliedRoll = Quaternion.Euler(0f, 0f, z * rollDegrees * rollScale);
        transform.localPosition = basePosition + new Vector3(x, y, 0f) * strength;
        transform.localRotation = look * appliedRoll;
    }

    /// <summary>Fades the shake out over its life, quickly at first then settling.</summary>
    private float Damping()
    {
        if (duration <= 0f) return 0f;
        float k = Mathf.Clamp01(timeLeft / duration);
        return k * k;
    }
}
