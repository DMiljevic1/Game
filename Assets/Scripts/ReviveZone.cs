using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "The safe house", as a box: a body has to lie inside one of these for Adrenaline to
/// bring it back. It is deliberately not the generator's radius -- being home is what
/// matters, not whether the fuel lasted.
///
/// A plain box in the object's local space rather than a trigger collider, because a
/// trigger the size of the house would catch PlayerInteractor's centre ray from outside
/// and dull its aim. The environment builder adds it to House, sized from the footprint.
///
/// Like Generator's registry, this is filled in OnEnable, which does not run in edit mode.
/// </summary>
[DisallowMultipleComponent]
public class ReviveZone : MonoBehaviour
{
    private static readonly List<ReviveZone> active = new List<ReviveZone>();

    [Tooltip("Centre of the box, in this object's local space.")]
    public Vector3 center = new Vector3(0f, 1.55f, 0f);

    [Tooltip("Size of the box, in this object's local space.")]
    public Vector3 size = new Vector3(14f, 3.5f, 11f);

    void OnEnable() { active.Add(this); }
    void OnDisable() { active.Remove(this); }

    /// <summary>True if the point is inside this box.</summary>
    public bool ContainsPoint(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point) - center;
        Vector3 half = size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>True if any safe house in the level contains the point.</summary>
    public static bool Contains(Vector3 point)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] != null && active[i].ContainsPoint(point)) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.45f, 1f, 0.6f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(center, size);
    }
}
