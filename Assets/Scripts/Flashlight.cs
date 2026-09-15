using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A flashlight the player picks up like any other Carryable and switches with its own
/// key while holding it.
///
/// It reads no input itself: PlayerInteractor routes the key, exactly as it does
/// for a door or the generator. The key is declared here so nothing can silently
/// steal it.
///
/// Co-op note: the light owns its own on/off state and nothing else reads it, so
/// this is the only value that has to replicate when netcode lands.
/// </summary>
[DisallowMultipleComponent]
public class Flashlight : Carryable
{
    [Header("Switch")]
    [Tooltip("Pressed while the flashlight is in the player's hands.")]
    public KeyCode toggleKey = KeyCode.X;
    public bool isOn = false;

    [Header("Parts")]
    [Tooltip("The spot light that forms the beam.")]
    public Light beam;
    [Tooltip("Lens renderer, swapped between the lit and dead materials.")]
    public Renderer lens;
    public Material lensOnMaterial;
    public Material lensOffMaterial;

    public event System.Action<bool> OnToggled = delegate { };

    protected override void Awake()
    {
        base.Awake();

        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "flashlight";
        if (beam == null)
        {
            Debug.LogError("Flashlight " + name + " has no beam light assigned; it will never light anything.", this);
        }
        ApplyState();
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        // In the hands the only thing to do is switch it; on the floor, pick it up.
        if (IsHeld) options.Add(new InteractionOption(toggleKey, isOn ? "Switch off flashlight" : "Switch on flashlight"));
        else base.GetOptions(interactor, options);
    }

    public override void Interact(PlayerInteractor interactor, KeyCode key)
    {
        if (IsHeld && key == toggleKey)
        {
            SetOn(!isOn);
            return;
        }
        base.Interact(interactor, key);
    }

    public void SetOn(bool on)
    {
        if (isOn == on) return;

        isOn = on;
        ApplyState();
        OnToggled(isOn);
    }

    // Dropping a lit flashlight leaves it lit on the ground: a light you can put down
    // is worth more than one that politely switches itself off.
    private void ApplyState()
    {
        if (beam != null) beam.enabled = isOn;

        if (lens != null)
        {
            Material m = isOn ? lensOnMaterial : lensOffMaterial;
            if (m != null) lens.sharedMaterial = m;
        }
    }
}
