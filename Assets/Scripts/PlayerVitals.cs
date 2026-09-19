using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whether the player is alive, and what happens when a monster reaches you.
///
/// There is no health: a monster's touch kills outright. So there is nothing to chip
/// away, nothing to regenerate and no number to read -- the only question the game ever
/// asks is whether the thing in the dark got to you, and the answer is always final.
///
/// Death is a state, not a teleport: a dead player stays dead until something revives
/// them. What that looks like -- the locked controls, the body left behind, the price of
/// bringing them back -- belongs to PlayerDeathLock, Revival and RunState, which all
/// react to the events here. This class knows nothing about any of them.
///
/// Co-op note: the alive/dead state is owned by the authority. The static registry is
/// every player in the level, not per-player state.
/// </summary>
[DisallowMultipleComponent]
public class PlayerVitals : MonoBehaviour
{
    // Every player in the level, so the run can ask "is anyone still standing?" without a
    // scene search. Scene-object registry, like Generator's.
    private static readonly List<PlayerVitals> all = new List<PlayerVitals>();

    /// <summary>Every player currently in the level, alive or dead.</summary>
    public static IReadOnlyList<PlayerVitals> All { get { return all; } }

    /// <summary>Any player died. Run-level systems listen here rather than to each player.</summary>
    public static event System.Action<PlayerVitals> AnyDied = delegate { };

    /// <summary>Any player got back up, by revive or by respawn.</summary>
    public static event System.Action<PlayerVitals> AnyRevived = delegate { };

    [Tooltip("Where Respawn puts the player. Only the singleplayer fallback uses it now; " +
             "a revive stands the player up where their body lies.")]
    public Transform respawnPoint;

    public event System.Action OnDied = delegate { };

    /// <summary>Back on their feet, by revive or by respawn.</summary>
    public event System.Action OnRevived = delegate { };

    private bool alive = true;
    private Vector3 lastDeathPosition;
    private float lastDeathYaw;
    private CharacterController controller;

    public bool IsAlive { get { return alive; } }

    /// <summary>
    /// Where the player was standing when they were last killed. Pinned before OnDied
    /// fires, so the carried-item drop and the body both land on the same spot.
    /// </summary>
    public Vector3 LastDeathPosition { get { return lastDeathPosition; } }

    /// <summary>Which way the player was facing when they were last killed, so the body lies that way.</summary>
    public float LastDeathYaw { get { return lastDeathYaw; } }

    // Authority seam, as with Wallet. Becomes IsServer once netcode is in.
    protected virtual bool HasAuthority { get { return true; } }

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (respawnPoint == null)
        {
            Debug.LogError("PlayerVitals on " + name + " has no respawn point; Respawn will stand the player up where they are.", this);
        }
    }

    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    /// <summary>
    /// Kills the player outright. The only way to die: there is no partial damage, so a
    /// future trap or fall calls this rather than taking a bite out of anything.
    /// </summary>
    public void Kill()
    {
        if (!HasAuthority) return;
        if (!alive) return;

        alive = false;
        Die();
    }

    private void Die()
    {
        // Pin the spot before anything reacts: the dropped item and the body both belong here.
        lastDeathPosition = transform.position;
        lastDeathYaw = transform.eulerAngles.y;

        // Run-level systems first, per-player systems second. Revival leaves the body on
        // AnyDied and takes the dead player's belongings onto it, so the interactor's own
        // death drop then finds nothing left in the hands. With no Revival, or no body
        // prefab, that drop still runs -- which is exactly the behaviour that came before.
        AnyDied(this);
        OnDied();
    }

    /// <summary>
    /// Stand the player up with their feet at <paramref name="feet"/>, facing
    /// <paramref name="yaw"/>. This is the revive: the spot is where the body lay.
    /// </summary>
    public void ReviveAt(Vector3 feet, float yaw)
    {
        // The controller's pivot is its centre, not its feet.
        float lift = controller != null
            ? controller.height * 0.5f - controller.center.y + controller.skinWidth
            : 1f;

        StandUp(feet + Vector3.up * lift, yaw);
    }

    /// <summary>Back at the respawn point. The singleplayer fallback's route.</summary>
    public void Respawn()
    {
        if (respawnPoint == null)
        {
            StandUp(transform.position, transform.eulerAngles.y);
            return;
        }

        StandUp(respawnPoint.position, respawnPoint.rotation.eulerAngles.y);
    }

    private void StandUp(Vector3 position, float yaw)
    {
        if (!HasAuthority) return;

        alive = true;

        // The controller overwrites transform moves, so switch it off for the teleport --
        // and leave it as it was: whether it is on is PlayerDeathLock's business.
        bool controllerWasOn = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (controller != null) controller.enabled = controllerWasOn;

        Physics.SyncTransforms();

        OnRevived();
        AnyRevived(this);
    }

    void OnGUI()
    {
        // With no health there is nothing to count down, so the row carries the one thing
        // about the player's condition that still changes: dead, or standing in the light.
        if (!alive)
        {
            Hud.Row(3, "DEAD", new Color(1f, 0.35f, 0.35f));
            return;
        }

        if (Generator.IsPointProtected(transform.position))
        {
            Hud.Row(3, "[SAFE]", new Color(0.55f, 1f, 0.6f));
        }
    }
}
