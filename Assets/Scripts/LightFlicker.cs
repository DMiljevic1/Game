using UnityEngine;

/// <summary>
/// An old lamp on a dying supply: a slow waver, and now and then a stutter where it nearly
/// goes out. Purely cosmetic — it only moves this light's intensity, so nothing in the
/// simulation (and certainly not the blind monster) can tell.
///
/// Not for generator-powered lights. The generator switches those with <c>enabled</c>, and
/// it must read as steady: protection has to be trustworthy at a glance.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [Tooltip("Fraction of the brightness the slow waver can take away.")]
    [Range(0f, 1f)] public float waver = 0.15f;
    [Tooltip("Wavers per second.")]
    public float waverSpeed = 2.5f;
    [Tooltip("Average stutters per second. 0.1 is one every ten seconds or so.")]
    public float stuttersPerSecond = 0.1f;
    [Tooltip("Seconds a stutter lasts.")]
    public float stutterLength = 0.25f;
    [Tooltip("How close to out the lamp drops during a stutter.")]
    [Range(0f, 1f)] public float stutterDepth = 0.85f;

    private Light lamp;
    private float baseIntensity;
    private float seed;
    private float stutterLeft;

    // Its own stream, so a lamp never shifts the global Random that the spawners draw from.
    private System.Random rng;

    void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
        rng = new System.Random(GetHashCode());
        seed = (float)rng.NextDouble() * 100f;
    }

    void OnDisable()
    {
        lamp.intensity = baseIntensity;
        stutterLeft = 0f;
    }

    void Update()
    {
        float k = 1f - waver * Mathf.PerlinNoise(seed, Time.time * waverSpeed);

        if (stutterLeft > 0f)
        {
            stutterLeft -= Time.deltaTime;
            // Chatter between dim and almost-out, like a loose contact.
            bool dip = Mathf.PerlinNoise(seed + 17f, Time.time * 30f) > 0.45f;
            k *= dip ? 1f - stutterDepth : 1f - stutterDepth * 0.4f;
        }
        else if (rng.NextDouble() < stuttersPerSecond * Time.deltaTime)
        {
            stutterLeft = stutterLength;
        }

        lamp.intensity = baseIntensity * k;
    }
}
