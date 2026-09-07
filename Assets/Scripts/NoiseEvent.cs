using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One thing that was heard: where it happened, how far it carries, and what made it.
/// Deliberately dumb data -- a noise knows nothing about the player, so a monster
/// reacting to one can never work backwards to a player's real position.
/// </summary>
public struct NoiseEvent
{
    /// <summary>Where the sound was made.</summary>
    public Vector3 position;

    /// <summary>How far it carries, in metres. This is the sound's loudness.</summary>
    public float radius;

    /// <summary>What made it, if anything. Used to ignore your own footsteps.</summary>
    public GameObject source;

    /// <summary>Time.time when it happened.</summary>
    public float time;

    public NoiseEvent(Vector3 position, float radius, GameObject source)
    {
        this.position = position;
        this.radius = radius;
        this.source = source;
        this.time = Time.time;
    }
}

/// <summary>Anything that can hear. Register with <see cref="Noise"/> to be told.</summary>
public interface INoiseListener
{
    void OnNoiseHeard(NoiseEvent noise);
}

/// <summary>
/// The noise bus. Anything that makes a sound calls <see cref="Emit"/>; anything with
/// ears registers itself and is told. Nobody polls, and no listener ever gets a
/// reference to who made the sound's transform -- only the position it happened at.
///
/// Same shape as Generator's static registry: a lookup table of live listeners, not
/// shared game state, so it stays netcode-safe. Emitting is local; only the authority
/// runs the monsters that act on what they hear.
/// </summary>
public static class Noise
{
    private static readonly List<INoiseListener> listeners = new List<INoiseListener>();

    /// <summary>Fires for every noise, heard or not. For debug overlays only.</summary>
    public static event System.Action<NoiseEvent> OnNoiseEmitted = delegate { };

    public static void Register(INoiseListener listener)
    {
        if (listener != null && !listeners.Contains(listener)) listeners.Add(listener);
    }

    public static void Unregister(INoiseListener listener)
    {
        listeners.Remove(listener);
    }

    /// <summary>Make a sound at a point in the world. This is the whole API.</summary>
    public static void Emit(Vector3 position, float radius, GameObject source)
    {
        Emit(new NoiseEvent(position, radius, source));
    }

    public static void Emit(NoiseEvent noise)
    {
        if (noise.radius <= 0f) return;

        OnNoiseEmitted(noise);

        // Backwards: a listener that dies while being told must not break the loop.
        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            if (IsDead(listeners[i])) { listeners.RemoveAt(i); continue; }
            listeners[i].OnNoiseHeard(noise);
        }
    }

    /// <summary>
    /// A destroyed MonoBehaviour is not reference-null, so an interface-typed null
    /// check misses it. Unwrap to Object and let Unity's own overload answer.
    /// </summary>
    private static bool IsDead(INoiseListener listener)
    {
        if (listener == null) return true;

        Object asObject = listener as Object;
        return !ReferenceEquals(asObject, null) && asObject == null;
    }
}
