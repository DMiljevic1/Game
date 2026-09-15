using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The finished key: the four fragments made whole. A plain <see cref="Carryable"/> with no
/// behaviour of its own -- what it is *for* is decided by the <see cref="Level2Door"/> it is
/// used on, exactly as a fuel can's meaning lives in the generator.
///
/// **There is deliberately only one way it can come into existence**, and that is
/// <see cref="KeyMaker"/> consuming all four fragments. It is in no loot table, no store
/// stock and nowhere in the scene, so it cannot be found, bought or stumbled upon.
///
/// It is pack-storable, so it can be carried out to the door alongside the UV flashlight and the
/// powder rather than filling the hands for the whole last trip.
/// </summary>
[DisallowMultipleComponent]
public class CompleteKey : Carryable
{
    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "finished key";
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld) return;
        options.Add(new InteractionOption(pickUpKey, "Take the " + itemName + PickUpRefusal(interactor)));
    }
}
