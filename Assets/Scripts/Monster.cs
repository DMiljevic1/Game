using UnityEngine;

/// <summary>
/// A night creature. It hunts the player, but cannot enter a running generator's
/// radius -- instead it gathers at the edge of the light and waits. That is the
/// whole lesson of the first night: the generator is what is holding them back,
/// and nothing has to say so out loud.
///
/// Movement is direct steering with a CharacterController rather than a NavMesh:
/// the level is flat and the controller slides along walls for free. Swap to
/// NavMeshAgent when levels get real geometry.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Monster : MonoBehaviour
{
    [Header("Movement")]
    public float roamSpeed = 2.2f;
    public float chaseSpeed = 4.0f;
    public float turnSpeed = 6f;
    public float gravity = -9.81f;

    [Header("Senses")]
    public float detectRadius = 32f;

    [Header("Attack")]
    public float attackRange = 1.7f;
    public float attackDamage = 25f;
    public float attackInterval = 1.2f;

    [Header("Safe zone")]
    [Tooltip("How far outside the protection radius they linger while waiting.")]
    public float standOffPadding = 1.5f;

    [Header("Doors")]
    [Tooltip("How far ahead it feels for a closed door.")]
    public float doorProbeDistance = 1.6f;
    [Tooltip("Seconds spent working at a door before it gives. The rattle is the warning.")]
    public float doorForceTime = 1.4f;

    [Header("Roaming")]
    public Vector3 roamCenter = Vector3.zero;
    public float roamRadius = 45f;
    public float roamArrivalDistance = 2.5f;

    private CharacterController controller;
    private Transform target;
    private PlayerVitals targetVitals;
    private Vector3 roamPoint;
    private float verticalVelocity;
    private float nextAttackTime;
    private DoorInteraction forcingDoor;
    private float forcingProgress;

    /// <summary>True while it is working at a door rather than moving.</summary>
    public bool IsForcingDoor { get { return forcingDoor != null; } }

    /// <summary>Assigned by the spawner so monsters do not each search the scene.</summary>
    public void SetTarget(Transform player)
    {
        target = player;
        targetVitals = player != null ? player.GetComponent<PlayerVitals>() : null;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        PickRoamPoint();
    }

    void Update()
    {
        // A closed door in the way is worked at, not walked around. This is only
        // reachable when the generator is off: while it runs they cannot get near
        // the house at all, so "they can open doors" is really "when the light dies".
        if (TryForceDoor()) return;

        Vector3 desired = DecideDestination();

        Vector3 flat = desired - transform.position;
        flat.y = 0f;

        float speed = IsHunting() ? chaseSpeed : roamSpeed;

        if (flat.sqrMagnitude > 0.04f)
        {
            Vector3 dir = flat.normalized;
            Move(dir * speed);
            Face(dir);
        }
        else
        {
            Move(Vector3.zero);
            if (target != null) Face(FlatDirectionTo(target.position));
        }

        TryAttack();
    }

    /// <summary>
    /// Feel ahead for a shut door and lean on it. Returns true while occupied with one,
    /// so the monster stands at the door instead of grinding into it.
    /// </summary>
    private bool TryForceDoor()
    {
        if (!IsHunting())
        {
            forcingDoor = null;
            return false;
        }

        if (forcingDoor != null)
        {
            // Someone opened it, or it vanished: stop and carry on.
            if (forcingDoor.IsOpen)
            {
                forcingDoor = null;
                return false;
            }

            forcingProgress += Time.deltaTime;
            if (forcingProgress >= doorForceTime)
            {
                forcingDoor.SetOpen(true);
                forcingDoor = null;
                return false;
            }

            // Hold position against the door while working at it.
            Move(Vector3.zero);
            if (target != null) Face(FlatDirectionTo(target.position));
            return true;
        }

        Vector3 origin = transform.position + Vector3.up * 1.0f;
        RaycastHit hit;
        if (!Physics.Raycast(origin, transform.forward, out hit, doorProbeDistance,
                             ~0, QueryTriggerInteraction.Ignore))
            return false;

        DoorInteraction door = hit.collider.GetComponentInParent<DoorInteraction>();
        if (door == null || door.IsOpen || !door.canBeForced) return false;

        forcingDoor = door;
        forcingProgress = 0f;
        return true;
    }

    private bool IsHunting()
    {
        if (target == null) return false;
        if (Vector3.Distance(transform.position, target.position) > detectRadius) return false;
        return !Generator.IsPointProtected(target.position);
    }

    private Vector3 DecideDestination()
    {
        if (target != null && Vector3.Distance(transform.position, target.position) <= detectRadius)
        {
            Generator protector = Generator.GetProtector(target.position);

            // Player is inside the light: close in, but stop at the boundary.
            if (protector != null) return StandOffPoint(protector);

            return target.position;
        }

        if (Vector3.Distance(transform.position, roamPoint) <= roamArrivalDistance) PickRoamPoint();
        return roamPoint;
    }

    /// <summary>The point on the safe-zone boundary nearest to this monster.</summary>
    private Vector3 StandOffPoint(Generator protector)
    {
        Vector3 centre = protector.transform.position;
        Vector3 outward = transform.position - centre;
        outward.y = 0f;

        if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;

        return centre + outward.normalized * (protector.protectionRadius + standOffPadding);
    }

    private void Move(Vector3 horizontal)
    {
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontal + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        // Hard guarantee: never end a frame inside a running generator's radius,
        // whatever the steering did or whatever a collision slid us into.
        Generator here = Generator.GetProtector(transform.position);
        if (here != null)
        {
            Vector3 centre = here.transform.position;
            Vector3 outward = transform.position - centre;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;

            Vector3 edge = centre + outward.normalized * (here.protectionRadius + standOffPadding);
            controller.enabled = false;
            transform.position = new Vector3(edge.x, transform.position.y, edge.z);
            controller.enabled = true;
        }
    }

    private void Face(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.001f) return;
        Quaternion want = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
        transform.rotation = Quaternion.Slerp(transform.rotation, want, Time.deltaTime * turnSpeed);
    }

    private Vector3 FlatDirectionTo(Vector3 p)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.normalized;
    }

    private void TryAttack()
    {
        if (targetVitals == null || !targetVitals.IsAlive) return;
        if (Time.time < nextAttackTime) return;
        if (Generator.IsPointProtected(target.position)) return;   // cannot be touched inside the light
        if (Vector3.Distance(transform.position, target.position) > attackRange) return;

        targetVitals.TakeDamage(attackDamage);
        nextAttackTime = Time.time + attackInterval;
    }

    private void PickRoamPoint()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector2 c = Random.insideUnitCircle * roamRadius;
            Vector3 p = roamCenter + new Vector3(c.x, transform.position.y, c.y);

            if (Generator.IsPointProtected(p)) continue;   // never wander into the light
            roamPoint = p;
            return;
        }

        roamPoint = transform.position;   // boxed in; stay put this cycle
    }
}
