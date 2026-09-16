using UnityEngine;

/// <summary>
/// Makes the woods darker the further this camera is from the house. Two dials, because
/// they do different jobs: less ambient makes what is near you darker, and thicker fog
/// shortens how far you can see. Only the first leaves the flashlight's reach alone, so most of
/// the darkening is ambient and the fog just closes the distance in.
///
/// One corner of the map is darker still: NightDepth asks <see cref="DarkQuarter"/> how much
/// of it is in force where the camera stands and folds that in. There it takes a third dial
/// as well -- the moon itself -- because ambient only empties the shadows while the moon goes
/// on picking out every surface facing it, and scaling ambient alone left that corner
/// perfectly readable. The quarter owns where it is and how dark it goes; this stays the only
/// thing in the game that writes ambient, fog or the moon's intensity.
///
/// Inside the mountain none of that is a gradient at all: <see cref="MountainInterior"/> is
/// asked how far in the camera is and the answer takes the sky away entirely, because there
/// is rock overhead and the flashlight is the only light in the place.
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
    [Tooltip("Fraction of the night ambient left in the deep forest. The flashlight is what makes up the difference.")]
    [Range(0f, 1f)] public float deepAmbient = 0.3f;

    [Header("Fog")]
    [Tooltip("Fog around the house. Thinner than the level's authored fog, so the yard and the treeline read by moonlight.")]
    public float clearDensity = 0.022f;
    [Tooltip("Fog in the deep forest. Keep it modest: fog dims the flashlight beam as much as the moonlight.")]
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

        float density = Mathf.Lerp(clearDensity, deepDensity, depth);
        float ambient = Mathf.Lerp(1f, deepAmbient, depth);

        // The dark quarter multiplies what the distance gradient has already left, so it reads
        // as this part of the woods being worse rather than as a second gradient of its own.
        float moon = 1f;

        DarkQuarter dark = DarkQuarter.Instance;
        if (dark != null)
        {
            float weight = dark.Weight(transform.position);
            ambient *= Mathf.Lerp(1f, dark.ambientScale, weight);
            density *= Mathf.Lerp(1f, dark.fogScale, weight);
            moon = Mathf.Lerp(1f, dark.moonScale, weight);
        }

        // Under rock, none of the above applies: there is no sky to be dark, so the cave is
        // not another gradient on top of the woods but a switch to black. Its fog is written
        // outright rather than scaled, because the beam is the only light in there and the
        // distance gradient's fog would swallow it.
        MountainInterior cave = MountainInterior.Instance;
        if (cave != null)
        {
            float weight = cave.Weight(transform.position);
            if (weight > 0f)
            {
                ambient *= Mathf.Lerp(1f, cave.ambientScale, weight);
                moon *= Mathf.Lerp(1f, cave.moonScale, weight);
                density = Mathf.Lerp(density, cave.fogDensity, weight);
            }
        }

        RenderSettings.fogDensity = density;

        TimeOfDay clock = TimeOfDay.Instance;
        if (clock == null) return;

        // The moon is written as the night's own intensity times the factor, never as a
        // multiple of what is already on the light -- the same never-compound rule the ambient
        // below follows. It is a per-viewer visual like the rest of this script: every client
        // lights its own scene, and nothing in the simulation reads a light's intensity.
        if (clock.sun != null) clock.sun.intensity = clock.nightIntensity * moon;
        RenderSettings.ambientSkyColor = clock.AmbientSky * ambient;
        RenderSettings.ambientEquatorColor = clock.AmbientEquator * ambient;
        RenderSettings.ambientGroundColor = clock.AmbientGround * ambient;
    }
}
