/// <summary>
/// Something a pinch of <see cref="MagicPowder"/> can bring into the world.
///
/// The powder deliberately knows nothing about what it might find: it pours, looks for
/// anything within reach that offers itself up through this interface, and tells it. So the
/// next hidden thing the game invents -- a door in a rock face, a hatch, a stash -- needs no
/// change at all to the item that finds it.
///
/// Whatever implements this needs a collider the powder's overlap search can actually hit,
/// and that collider has to be live **while the thing is still hidden**. A trigger volume is
/// the usual answer (see the reveal circle's, and <see cref="Concealed.probeVolume"/>).
/// </summary>
public interface IPowderRevealable
{
    /// <summary>True once the powder has found it. One way: a thing that has been found stays found.</summary>
    bool HasBeenRevealed { get; }

    /// <summary>The powder has landed within reach. Only <see cref="MagicPowder"/> calls this.</summary>
    void Reveal();
}
