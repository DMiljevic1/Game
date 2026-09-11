using UnityEngine;

/// <summary>
/// Makes the woods darker the further this camera is from the house. Two dials, because
/// they do different jobs: less ambient makes what is near you darker, and thicker fog
/// shortens how far you can see. Only the first leaves the torch's reach alone, so most of
/// the darkening is ambient and the fog just closes the distance in.
///
/// TimeOfDay owns the night's base ambient; this writes that base, scaled for where the
/// camera stands, every LateUpdate, so it can never compound. It only changes how this
/// camera sees the world — nothing in the simulation reads fog or ambient — so in co-op
/// every client runs its own and there is nothing to replicate.
/// </summary>
[DisallowMultipleComponent]
public class NightDepth : MonoBehaviour
{
    [Tooltip("What the darkness is measured from. The generator, because its light is what the player walks back to.")]
    public Transform centre;
    [Tooltip("Within this many metres of the centre the night is at its lightest.")]
    public float clearRadius = 18f;
    [Tooltip("From this far out it is at its darkest: the deep forest.")]
    public float deepRadius = 45f;

    [Header("Ambient")]
    [Tooltip("Fraction of the night ambient left in the deep forest. The torch is what makes up the difference.")]
    [Range(0f, 1f)] public float deepAmbient = 0.3f;

    [Header("Fog")]
    [Tooltip("Fog around the house. Thinner than the level's authored fog, so the yard and the treeline read by moonlight.")]
    public float clearDensity = 0.022f;
    [Tooltip("Fog in the deep forest. Keep it modest: fog dims the torch beam as much as the moonlight.")]
    public float deepDensity = 0.055f;

    private float levelDensity;

    void Awake()
    {
        // The level's own fog, as the environment builder authored it.
        levelDensity = RenderSettings.fogDensity;

        if (centre == null)
        {
            Debug.LogError("NightDepth has no centre; the forest will not darken with distance.", this);
        }
    }

    void OnDisable()
    {
        // Hand back the authored fog and the unscaled night ambient.
        RenderSettings.fogDensity = levelDensity;

        TimeOfDay clock = TimeOfDay.Instance;
        if (clock != null) clock.ApplyLighting();
    }

    void LateUpdate()
    {
        if (centre == null) return;

        Vector3 offset = transform.position - centre.position;
        offset.y = 0f;
        float depth = Mathf.InverseLerp(clearRadius, deepRadius, offset.magnitude);

        // Smoothstep, so walking into the trees is a gradual closing-in rather than a band.
        depth = depth * depth * (3f - 2f * depth);

        RenderSettings.fogDensity = Mathf.Lerp(clearDensity, deepDensity, depth);

        TimeOfDay clock = TimeOfDay.Instance;
        if (clock == null) return;

        float ambient = Mathf.Lerp(1f, deepAmbient, depth);
        RenderSettings.ambientSkyColor = clock.AmbientSky * ambient;
        RenderSettings.ambientEquatorColor = clock.AmbientEquator * ambient;
        RenderSettings.ambientGroundColor = clock.AmbientGround * ambient;
    }
}
