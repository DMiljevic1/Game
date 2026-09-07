using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    public float sprintSpeed = 9f;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float gravity = -9.81f;
    public KeyCode jumpKey = KeyCode.Space;
    public float jumpHeight = 1.2f;

    private CharacterController controller;
    private Vector3 verticalVelocity;

    // What is in the hands, as speed only. Pushed in by PlayerInteractor when an item
    // is picked up and cleared on every way it leaves the hands, so movement never has
    // to look at the carried item -- and can never be left slow with empty hands.
    private CarryLoad load = CarryLoad.None;

    /// <summary>The penalty currently applied. CarryLoad.None when carrying nothing.</summary>
    public CarryLoad Load { get { return load; } }

    /// <summary>Walking speed right now, after whatever is being carried.</summary>
    public float CurrentWalkSpeed { get { return speed * load.moveMultiplier; } }

    /// <summary>False while holding something too big to run with.</summary>
    public bool CanSprint { get { return load.allowSprint; } }

    void Start()
    {
        controller = GetComponent<CharacterController>();
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

    void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // A load that forbids sprinting swallows the key entirely, so a heavy item is
        // slow whatever the player holds down.
        bool sprinting = load.allowSprint && Input.GetKey(sprintKey);

        float currentSpeed = sprinting
            ? sprintSpeed * load.sprintMultiplier
            : speed * load.moveMultiplier;

        // Sample this once, before any Move. isGrounded is only meaningful straight
        // after the previous Move; calling Move again re-evaluates and can clear it.
        bool grounded = controller.isGrounded;

        // Keep a little downward bias while grounded so the collision sweep stays in
        // contact with the floor, but never fight an upward jump.
        if (grounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f;
        }

        // Ground-only, so holding the key can't climb walls.
        if (grounded && Input.GetKeyDown(jumpKey))
        {
            // Velocity that peaks at exactly jumpHeight under the current gravity.
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity.y += gravity * Time.deltaTime;

        // Move relative to where the player is facing (yaw), so it matches mouse look.
        Vector3 move = transform.right * x + transform.forward * z;

        // One Move per frame: two calls make the second one's isGrounded unreliable,
        // and a near-zero first call can be dropped entirely by minMoveDistance.
        controller.Move((move * currentSpeed + verticalVelocity) * Time.deltaTime);
    }
}
