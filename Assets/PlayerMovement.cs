using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Tooltip("Walking speed. Just under the monster's chaseSpeed (4): walking never gets you away, " +
             "only sound, corners and doors do.")]
    public float speed = 3.5f;
    [Tooltip("Faster than a chasing monster, but only while stamina lasts.")]
    public float sprintSpeed = 6f;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float gravity = -9.81f;
    public KeyCode jumpKey = KeyCode.Space;
    public float jumpHeight = 1.2f;

    [Header("Stamina")]
    [Tooltip("Seconds of sprint from full = maxStamina / sprintStaminaDrain.")]
    public float maxStamina = 100f;

    [Tooltip("Stamina spent per second of sprinting.")]
    public float sprintStaminaDrain = 20f;

    [Tooltip("Stamina regained per second once staminaRegenDelay has passed. Keep it at or under " +
             "drain x (chaseSpeed - speed) / (sprintSpeed - chaseSpeed) -- 5 with the defaults -- or " +
             "sprinting in bursts averages faster than the monster and outruns it forever.")]
    public float staminaRegenRate = 5f;

    [Tooltip("Seconds after the last sprinting frame before stamina starts coming back. Tapping the " +
             "key faster than this earns nothing back, so it cannot be gamed.")]
    public float staminaRegenDelay = 1.5f;

    [Tooltip("Once stamina runs dry, sprinting stays locked until it has refilled to this, so a held " +
             "key cannot stutter along on every scrap regained.")]
    public float exhaustedResumeStamina = 25f;

    [Header("Carrying something heavy")]
    [Tooltip("Walking speed while a heavy load is in the hands (the television, Tom's case, a body). " +
             "Heavy also means no sprint and no jump. Keep it well under the monster's chaseSpeed (4): " +
             "the heavy prize is the one thing you cannot outrun.")]
    public float heavyWalkSpeed = 2.8f;

    [Header("Crouch")]
    [Tooltip("Toggles crouching. A crouched body makes no sound -- see NoiseEmitter.")]
    public KeyCode crouchKey = KeyCode.C;

    [Tooltip("Walking speed while crouched with empty hands. Whatever is carried slows it by the same " +
             "fraction it slows walking, so a heavy load crouched is slower than either alone.")]
    public float crouchSpeed = 2f;

    [Tooltip("Capsule height while crouched. The feet stay put, so the eye drops by the difference.")]
    public float crouchHeight = 1.2f;

    [Tooltip("How fast the eye moves between standing and crouched height, in metres per second.")]
    public float eyeMoveSpeed = 5f;

    [Tooltip("The camera that drops when crouching. Found among the children if left empty.")]
    public Transform eye;

    [Tooltip("The avatar's solid capsule, resized with the controller so a crouched body is crouched " +
             "for everything else too. Optional.")]
    public CapsuleCollider bodyCollider;

    private CharacterController controller;
    private Vector3 verticalVelocity;

    // What is in the hands, as speed only. Pushed in by PlayerInteractor when an item
    // is picked up and cleared on every way it leaves the hands, so movement never has
    // to look at the carried item -- and can never be left slow with empty hands.
    private CarryLoad load = CarryLoad.None;

    // The toggle is what the player asked for; crouching is what the body is doing. They
    // differ only while there is no room to stand, and everything else reads the second.
    private bool wantsCrouch;
    private bool crouching;

    private float standHeight;
    private Vector3 standCenter;
    private float bodyStandHeight;
    private Vector3 bodyStandCenter;

    // How far the eye has been lowered so far. Moved by the difference only, so a camera
    // shake offsetting the same transform is carried along rather than overwritten.
    private float appliedEyeDrop;

    private readonly Collider[] headroomBuffer = new Collider[16];

    private float stamina;
    private bool exhausted;
    private float regenWait;

    /// <summary>The penalty currently applied. CarryLoad.None when carrying nothing.</summary>
    public CarryLoad Load { get { return load; } }

    /// <summary>Stamina left, 0 to maxStamina.</summary>
    public float Stamina { get { return stamina; } }

    /// <summary>True from running dry until exhaustedResumeStamina has come back.</summary>
    public bool IsExhausted { get { return exhausted; } }

    /// <summary>True while the body is actually crouched. This is what silences footsteps.</summary>
    public bool IsCrouching { get { return crouching; } }

    /// <summary>True while crouching only because there is no room to stand up yet.</summary>
    public bool IsStuckCrouching { get { return crouching && !wantsCrouch; } }

    /// <summary>Walking speed right now, after whatever is being carried and crouching.</summary>
    public float CurrentWalkSpeed
    {
        get
        {
            float walk = load.heavy ? heavyWalkSpeed : speed * load.moveMultiplier;
            return crouching && speed > 0f ? walk * (crouchSpeed / speed) : walk;
        }
    }

    /// <summary>False while holding something too big to run with, or crouched.</summary>
    public bool CanSprint { get { return load.allowSprint && !load.heavy && !crouching; } }

    /// <summary>False while holding something heavy, or crouched.</summary>
    public bool CanJump { get { return !load.heavy && !crouching; } }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        stamina = maxStamina;
        standHeight = controller.height;
        standCenter = controller.center;

        if (bodyCollider != null)
        {
            bodyStandHeight = bodyCollider.height;
            bodyStandCenter = bodyCollider.center;
        }

        if (eye == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) eye = cam.transform;
        }
        if (eye == null)
        {
            Debug.LogError("PlayerMovement on " + name + " has no eye; crouching will not lower the view.", this);
        }
    }

    /// <summary>Weigh the player down. Call it once, when the item is taken into the hands.</summary>
    public void SetCarryLoad(CarryLoad newLoad)
    {
        load = newLoad;
    }

    /// <summary>Back to empty-handed speed. Dropping, selling and dying all end here.</summary>
    public void ClearCarryLoad()
    {
        load = CarryLoad.None;
    }

    /// <summary>
    /// Ask for the crouch without a keyboard, the same way Step can be driven without one.
    /// It is a request, not a command: standing up still waits for headroom, so this can
    /// never push the capsule through a ceiling. For test harnesses now, and for whatever
    /// feeds a remote player's input later.
    /// </summary>
    public void SetCrouch(bool crouch)
    {
        wantsCrouch = crouch;
    }

    void Update()
    {
        // Input is read here and nowhere below, so the step itself can be driven without a keyboard.
        if (Input.GetKeyDown(crouchKey)) wantsCrouch = !wantsCrouch;

        Step(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"),
             Input.GetKey(sprintKey), Input.GetKeyDown(jumpKey), Time.deltaTime);
    }

    private void Step(float x, float z, bool sprintHeld, bool jumpPressed, float dt)
    {
        UpdateCrouch(dt);

        // A load or a crouch that forbids sprinting swallows the key entirely, so holding it
        // does nothing at all instead of feeling broken. Holding it while standing still costs nothing.
        bool moving = x * x + z * z > 0.01f;
        bool sprinting = CanSprint && sprintHeld && moving && !exhausted && stamina > 0f;
        UpdateStamina(sprinting, dt);

        float currentSpeed = sprinting
            ? sprintSpeed * load.sprintMultiplier
            : CurrentWalkSpeed;

        // Sample this once, before any Move. isGrounded is only meaningful straight
        // after the previous Move; calling Move again re-evaluates and can clear it.
        bool grounded = controller.isGrounded;

        // Keep a little downward bias while grounded so the collision sweep stays in
        // contact with the floor, but never fight an upward jump.
        if (grounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f;
        }

        // Ground-only, so holding the key can't climb walls. Swallowed like sprint.
        if (grounded && jumpPressed && CanJump)
        {
            // Velocity that peaks at exactly jumpHeight under the current gravity.
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity.y += gravity * dt;

        // Move relative to where the player is facing (yaw), so it matches mouse look. Clamped so
        // a diagonal is no faster than straight ahead -- unclamped, W+D walked at 1.41x and outran the monster.
        Vector3 move = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);

        // One Move per frame: two calls make the second one's isGrounded unreliable,
        // and a near-zero first call can be dropped entirely by minMoveDistance.
        controller.Move((move * currentSpeed + verticalVelocity) * dt);
    }

    /// <summary>
    /// Sprinting drains; anything else refills, but only after staminaRegenDelay without a
    /// sprinting frame -- so every tap restarts the wait and rapid tapping regains nothing.
    /// </summary>
    private void UpdateStamina(bool sprinting, float dt)
    {
        if (sprinting)
        {
            stamina = Mathf.Max(0f, stamina - sprintStaminaDrain * dt);
            regenWait = staminaRegenDelay;
            if (stamina <= 0f) exhausted = true;
            return;
        }

        if (regenWait > 0f)
        {
            regenWait -= dt;
            return;
        }

        stamina = Mathf.Min(maxStamina, stamina + staminaRegenRate * dt);
        if (exhausted && stamina >= Mathf.Min(exhaustedResumeStamina, maxStamina)) exhausted = false;
    }

    /// <summary>
    /// Follow the toggle, except that standing up waits for headroom: pressing C under a
    /// table queues the stand and it happens as soon as there is room.
    /// </summary>
    private void UpdateCrouch(float dt)
    {
        if (wantsCrouch && !crouching)
        {
            crouching = true;
            SetCapsuleHeight(CrouchedHeight);
        }
        else if (!wantsCrouch && crouching && HasHeadroom())
        {
            crouching = false;
            SetCapsuleHeight(standHeight);
        }

        float targetDrop = crouching ? standHeight - CrouchedHeight : 0f;
        float drop = Mathf.MoveTowards(appliedEyeDrop, targetDrop, eyeMoveSpeed * dt);
        if (eye != null) eye.localPosition += Vector3.down * (drop - appliedEyeDrop);
        appliedEyeDrop = drop;
    }

    // A capsule cannot be shorter than it is wide.
    private float CrouchedHeight
    {
        get { return Mathf.Clamp(crouchHeight, controller.radius * 2f + 0.01f, standHeight); }
    }

    /// <summary>Resize with the feet fixed, so crouching never lifts or sinks the player.</summary>
    private void SetCapsuleHeight(float height)
    {
        float shrink = standHeight - height;
        controller.height = height;
        controller.center = standCenter + Vector3.down * (shrink * 0.5f);

        if (bodyCollider != null)
        {
            bodyCollider.height = bodyStandHeight - shrink;
            bodyCollider.center = bodyStandCenter + Vector3.down * (shrink * 0.5f);
        }
    }

    /// <summary>Is the space the standing capsule would add above the crouched one clear?</summary>
    private bool HasHeadroom()
    {
        float radius = controller.radius * 0.95f;
        Vector3 feet = transform.TransformPoint(controller.center) + Vector3.down * (controller.height * 0.5f);

        // From the crouched head's cap up to the standing head. The lower sphere dips back
        // inside our own (wider) capsule, where nothing solid can be, so only what is
        // actually overhead can count against standing.
        Vector3 low = feet + Vector3.up * (controller.height - radius + 0.05f);
        Vector3 high = feet + Vector3.up * (standHeight - radius);
        if (high.y < low.y) high = low;

        int count = Physics.OverlapCapsuleNonAlloc(low, high, radius, headroomBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (headroomBuffer[i].transform.root == transform.root) continue;   // our own body and hands
            return false;
        }
        return true;
    }

    void OnGUI()
    {
        // Silence is the whole point of crouching, so say so -- and say why a C press
        // under a table has not stood you up yet.
        if (crouching)
        {
            Hud.Row(0, IsStuckCrouching ? "Crouching - no room to stand" : "Crouching - silent",
                    new Color(0.7f, 0.85f, 1f));
        }

        // Only while it is being spent or refilled; a dead sprint key with no explanation reads as a bug.
        if (stamina < maxStamina)
        {
            Hud.Row(1, exhausted
                        ? string.Format("Stamina {0:0}% - out of breath", stamina / maxStamina * 100f)
                        : string.Format("Stamina {0:0}%", stamina / maxStamina * 100f),
                    exhausted ? new Color(1f, 0.55f, 0.35f) : new Color(0.85f, 0.95f, 0.7f));
        }
    }
}
