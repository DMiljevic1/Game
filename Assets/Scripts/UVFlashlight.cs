using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ultraviolet flashlight. It is an ordinary <see cref="Flashlight"/> in every way that
/// matters -- picked up, carried, switched with its own key by the interaction router, dark
/// while stowed -- so it inherits the whole of that behaviour rather than restating it.
///
/// The one thing it adds is a **registry of lit UV beams**, which is how anything painted in
/// ultraviolet knows whether it is currently being looked at. Same shape as
/// <see cref="Generator"/>'s and <see cref="Noise"/>'s registries: a lookup table of live
/// objects, not shared game state, so it stays netcode-safe -- each client lights its own
/// scene and reveals its own marks.
///
/// Nothing else in the game reads UV, and the monster is blind, so this reveals things to
/// the *player* and changes nothing about the simulation.
/// </summary>
[DisallowMultipleComponent]
public class UVFlashlight : Flashlight
{
    // Every UV flashlight that currently exists and is awake. Enabled/disabled rather than
    // on/off: stowing deactivates the object, which is exactly when it should stop counting.
    private static readonly List<UVFlashlight> active = new List<UVFlashlight>();

    /// <summary>True if any UV beam anywhere is switched on. The cheap early-out.</summary>
    public static bool AnyLit
    {
        get
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null) { active.RemoveAt(i); continue; }
                if (active[i].isOn) return true;
            }
            return false;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item" || itemName == "flashlight")
        {
            itemName = "UV flashlight";
        }
    }

    void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    void OnDisable() { active.Remove(this); }

    /// <summary>
    /// Is this point inside a lit UV beam right now?
    ///
    /// The cone is read from the beam <see cref="Light"/> itself -- its range and spot angle
    /// -- so what is revealed is exactly what the player can see lit up. Retuning the light
    /// retunes the reveal for free, and the two can never drift apart.
    /// </summary>
    public static bool Illuminates(Vector3 point)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            UVFlashlight flashlight = active[i];
            if (flashlight == null) { active.RemoveAt(i); continue; }
            if (!flashlight.isOn || flashlight.beam == null) continue;

            Transform t = flashlight.beam.transform;
            Vector3 delta = point - t.position;

            float distance = delta.magnitude;
            if (distance > flashlight.beam.range) continue;
            if (distance < 0.05f) return true;                 // all but standing on it

            if (Vector3.Angle(t.forward, delta) <= flashlight.beam.spotAngle * 0.5f) return true;
        }
        return false;
    }
}
