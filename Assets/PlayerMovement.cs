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

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        float currentSpeed = Input.GetKey(sprintKey) ? sprintSpeed : speed;

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
