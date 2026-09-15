using UnityEngine;

/// <summary>
/// Turns movement into noise. Put it on anything with a CharacterController that
/// should be audible -- the player now, a wandering NPC later.
///
/// It reads the controller's actual speed rather than the input keys, so it costs
/// PlayerMovement nothing and stays right if movement is ever rewritten. Standing
/// still emits nothing at all: silence is the player's only real defence against
/// something that hunts by ear -- and crouching only ever makes you *quiet*, never
/// inaudible, so creeping is a strong option rather than an off switch for the monster.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NoiseEmitter : MonoBehaviour
{
    [Header("Footsteps")]
    [Tooltip("Below this speed nothing is heard. Creeping is free.")]
    public float walkThreshold = 0.6f;
    [Tooltip("At or above this speed the step counts as a run. Between PlayerMovement.speed (3.5) and " +
             "sprintSpeed (6) -- and under a Medium load's 5.4 sprint -- or sprinting would sound like walking.")]
    public float sprintThreshold = 4.7f;

    [Tooltip("How far a walking step carries, in metres.")]
    public float walkNoiseRadius = 8f;
    [Tooltip("How far a running step carries. This is what gets you caught.")]
    public float sprintNoiseRadius = 20f;

    [Tooltip("Seconds between steps at walking pace. Running steps come faster.")]
    public float walkStepInterval = 0.55f;
    public float sprintStepInterval = 0.32f;

    [Header("Landing")]
    [Tooltip("How far the thump of landing from a jump carries.")]
    public float landNoiseRadius = 14f;

    [Header("Crouching")]
    [Tooltip("How far a crouched footstep carries. Small enough that something has to be nearly on " +
             "top of you to hear it at all -- so crouching is very quiet, but not silent, which " +
             "would make it a way to ignore the monster outright. Standing still while crouched IS " +
             "silent, because that falls under walkThreshold.")]
    public float crouchNoiseRadius = 2.5f;

    [Tooltip("Seconds between crouched steps. Slower than walking: creeping is deliberate.")]
    public float crouchStepInterval = 0.85f;

    [Tooltip("Multiplies every one-off Emit while crouched -- the interactor's handling noise above " +
             "all. 0.31 muffles its 12 m to under 4, so working a door beside something is still a " +
             "risk, just a much smaller one.")]
    [Range(0f, 1f)] public float crouchNoiseScale = 0.31f;

    [Tooltip("The body whose crouch quietens all of this. Found on this object if left empty; " +
             "without one nothing is ever muffled, so an NPC needs none.")]
    public PlayerMovement movement;

    private CharacterController controller;
    private float nextStepTime;
    private bool wasGrounded = true;

    /// <summary>True while this body is creeping, so everything it does is quietened.</summary>
    public bool IsMuffled { get { return movement != null && movement.IsCrouching; } }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        bool grounded = controller.isGrounded;

        // A landing is one loud thump, not a step.
        if (grounded && !wasGrounded) Emit(landNoiseRadius);
        wasGrounded = grounded;

        if (!grounded) return;   // in the air you make no footfalls

        Vector3 flat = controller.velocity;
        flat.y = 0f;
        float speed = flat.magnitude;

        // Standing still is silent whatever the posture. This is the player's real zero.
        if (speed < walkThreshold) return;

        bool crouched = IsMuffled;
        bool running = !crouched && speed >= sprintThreshold;

        float interval = crouched ? crouchStepInterval
                       : running ? sprintStepInterval : walkStepInterval;

        if (Time.time < nextStepTime) return;
        nextStepTime = Time.time + interval;

        // Each gait is its own plain radius rather than a scaled walk, so the three are
        // three numbers a listener can tell apart -- see Monster.NoiseWeight.
        float radius = crouched ? crouchNoiseRadius
                     : running ? sprintNoiseRadius : walkNoiseRadius;

        // Straight to the bus: the crouched case is already the radius above, so passing
        // it through Emit would scale it a second time.
        Noise.Emit(transform.position, radius, gameObject);
    }

    /// <summary>
    /// Make a one-off sound from this object -- interacting, dropping something. The one
    /// gate for everything else this body does, so a crouch can never miss a sound:
    /// crouched, the radius is scaled rather than thrown away, so the noise still exists
    /// and the debug overlays still show it.
    /// </summary>
    public void Emit(float radius)
    {
        if (IsMuffled) radius *= crouchNoiseScale;
        if (radius <= 0f) return;
        Noise.Emit(transform.position, radius, gameObject);
    }
}
