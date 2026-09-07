using UnityEngine;

/// <summary>
/// Turns movement into noise. Put it on anything with a CharacterController that
/// should be audible -- the player now, a wandering NPC later.
///
/// It reads the controller's actual speed rather than the input keys, so it costs
/// PlayerMovement nothing and stays right if movement is ever rewritten. Standing
/// still emits nothing at all: silence is the player's only real defence against
/// something that hunts by ear.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NoiseEmitter : MonoBehaviour
{
    [Header("Footsteps")]
    [Tooltip("Below this speed nothing is heard. Creeping is free.")]
    public float walkThreshold = 0.6f;
    [Tooltip("At or above this speed the step counts as a run.")]
    public float sprintThreshold = 6.5f;

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

    private CharacterController controller;
    private float nextStepTime;
    private bool wasGrounded = true;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
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

        if (speed < walkThreshold) return;

        bool running = speed >= sprintThreshold;
        float interval = running ? sprintStepInterval : walkStepInterval;

        if (Time.time < nextStepTime) return;
        nextStepTime = Time.time + interval;

        Emit(running ? sprintNoiseRadius : walkNoiseRadius);
    }

    /// <summary>Make a one-off sound from this object -- interacting, dropping something.</summary>
    public void Emit(float radius)
    {
        Noise.Emit(transform.position, radius, gameObject);
    }
}
