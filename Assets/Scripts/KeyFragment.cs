using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One of the four broken pieces of the gate key. A plain <see cref="Carryable"/> and
/// nothing else -- it has no objective state and no idea how many of it there are. What a
/// fragment *means* is decided by the <see cref="Gate"/> it is fitted into.
///
/// It is small and pack-storable on purpose: a fragment costs you one of the four slots, so
/// "carry this back or carry the radio back" is a decision made in the field rather than in
/// a menu.
///
/// Co-op note: an ordinary world object with no per-player state, so it can be handed to,
/// dropped for or picked up by anyone on the team, exactly like a fuel can.
/// </summary>
[DisallowMultipleComponent]
public class KeyFragment : Carryable
{
    [Tooltip("Which of the four pieces this is, 0 upwards. Set by Expedition as it scatters " +
             "them; it only has to be unique.")]
    public int index;

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrEmpty(itemName) || itemName == "Item") itemName = "key fragment";
    }

    /// <summary>Number it as it is placed, so the prompt and the gate can tell them apart.</summary>
    public void Describe(int fragmentIndex)
    {
        index = fragmentIndex;
        itemName = "key fragment";
    }

    public override void GetOptions(PlayerInteractor interactor, List<InteractionOption> options)
    {
        if (IsHeld) return;
        options.Add(new InteractionOption(pickUpKey, "Take the " + itemName + PickUpRefusal(interactor)));
    }
}
