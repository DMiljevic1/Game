using UnityEngine;

/// <summary>
/// The inside of the mountain: the one place in the level with no sky over it.
///
/// Out in the woods the night is something you can walk by -- there is moon on the trunks and
/// the ambient gradient keeps the ground readable, and <see cref="DarkQuarter"/> only takes
/// that away by degrees. Under rock there is nothing to take away by degrees: the cave is
/// **black**, and the flashlight is the only thing in it. That is the whole point of the
/// place, and it is why the dark here is a switch rather than another gradient.
///
/// It owns no geometry and changes nothing. It answers one question -- how far inside am I --
/// and <see cref="NightDepth"/>, which stays the only thing in the game that writes ambient,
/// fog or the moon, folds the answer in. So the darkness is per viewer exactly like the rest
/// of that script: every client lights its own scene and there is nothing to replicate.
///
/// The volumes are plain empty transforms scaled to the box they stand for -- the same
/// unit-cube convention the environment builder's primitives use -- so the cave's shape is
/// described where the cave is built and never authored twice. A point is inside when it is
/// inside any of them.
/// </summary>
[DisallowMultipleComponent]
public class MountainInterior : MonoBehaviour
{
    public static MountainInterior Instance { get; private set; }

    [Tooltip("One empty transform per chamber and passage, scaled to the box it stands for. " +
             "Built by the environment builder from the same skeleton it carves the cave out of.")]
    public Transform[] volumes;

    [Tooltip("Metres of fade across the horizontal faces, so the mouth of the cave is a " +
             "closing-in rather than a step across a line. The interior faces are buried in " +
             "rock, so this only ever shows at the entrance.")]
    public float softness = 4f;

    [Header("How dark")]
    [Tooltip("What is left of the ambient light well inside. Zero on purpose: there is no sky " +
             "here, so there is nothing for ambient to be.")]
    [Range(0f, 1f)] public float ambientScale = 0f;

    [Tooltip("What is left of the moon. Zero for the same reason -- rock overhead.")]
    [Range(0f, 1f)] public float moonScale = 0f;

    [Tooltip("Fog inside. Thin: fog dims the flashlight beam as much as anything else, and in " +
             "here the beam is all the player has.")]
    public float fogDensity = 0.018f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("A second MountainInterior exists on " + name + "; destroying it. " +
                           "There must be exactly one.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// How much of the cave's dark is in force at this point: 0 out in the woods, 1 well
    /// inside. Pure query -- it changes nothing, so a HUD, a second camera or a future
    /// monster that only hunts in here can all ask it freely.
    /// </summary>
    public float Weight(Vector3 point)
    {
        if (volumes == null) return 0f;

        float best = 0f;
        for (int i = 0; i < volumes.Length; i++)
        {
            float w = WeightIn(volumes[i], point);
            if (w > best) best = w;
            if (best >= 1f) return 1f;
        }
        return best;
    }

    /// <summary>True anywhere the cave's dark has any hold at all.</summary>
    public bool Contains(Vector3 point)
    {
        return Weight(point) > 0f;
    }

    private float WeightIn(Transform volume, Vector3 point)
    {
        if (volume == null) return 0f;

        // The unit cube the transform stands for: inside is |local| < 0.5 on every axis.
        Vector3 local = volume.InverseTransformPoint(point);
        if (Mathf.Abs(local.x) >= 0.5f || Mathf.Abs(local.y) >= 0.5f || Mathf.Abs(local.z) >= 0.5f) return 0f;

        // Only the horizontal faces fade. A cave is a few metres from floor to ceiling, so
        // fading on height as well would leave a standing player permanently half-lit.
        Vector3 size = volume.lossyScale;
        float inside = Mathf.Min((0.5f - Mathf.Abs(local.x)) * Mathf.Abs(size.x),
                                 (0.5f - Mathf.Abs(local.z)) * Mathf.Abs(size.z));

        float t = Mathf.Clamp01(inside / Mathf.Max(0.01f, softness));
        return t * t * (3f - 2f * t);
    }

    void OnDrawGizmosSelected()
    {
        if (volumes == null) return;

        Gizmos.color = new Color(0.35f, 0.3f, 0.45f, 0.5f);
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] == null) continue;
            Gizmos.matrix = volumes[i].localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
