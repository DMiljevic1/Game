using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A can of generator fuel. Carried to the generator and poured in.
/// Emptying it leaves the can in the world rather than deleting it, so players can
/// see at a glance which cans they have already used.
/// </summary>
public class FuelCan : Carryable
{
    [Tooltip("Fuel in the can. The generator holds 100, a night costs about 75.")]
    public float fuel = 40f;

    [Tooltip("Tint applied to the can once it has been emptied.")]
    public Color emptyColor = new Color(0.35f, 0.33f, 0.30f);

    public bool IsEmpty { get { return fuel <= 0f; } }

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "fuel can";
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld) return;

        options.Add(new InteractionOption(pickUpKey, IsEmpty
            ? "Pick up empty can"
            : string.Format("Pick up {0} ({1:0} fuel)", itemName, fuel)));
    }

    /// <summary>Pour everything into a generator. Returns how much was actually accepted.</summary>
    public float PourInto(Generator generator)
    {
        if (generator == null || IsEmpty) return 0f;

        float accepted = generator.AddFuel(fuel);
        fuel -= accepted;

        if (IsEmpty) MarkEmpty();
        return accepted;
    }

    private void MarkEmpty()
    {
        itemName = "empty can";

        // Cheap visual tell until there is real art.
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            var m = r.material;      // instance, so other cans are unaffected
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", emptyColor);
            else m.color = emptyColor;
        }
    }
}
