using UnityEngine;

/// <summary>Where the monster's head is at, in one word.</summary>
public enum MonsterState
{
    /// <summary>Walking its round, hearing nothing.</summary>
    Patrol,
    /// <summary>Heard something and is walking to where it thinks it came from.</summary>
    Alerted,
    /// <summary>Standing at the noise, turning, listening for another one.</summary>
    Investigate,
    /// <summary>Heard enough, close enough, often enough. Moves fast.</summary>
    Chase
}

/// <summary>
/// A night creature that is BLIND. It has no vision and never reads the player's
/// transform -- everything it knows arrives through <see cref="Noise"/> as a position
/// and a loudness, blurred by distance. Stand still and make no sound and it walks
/// straight past you. A torch changes nothing.
///
/// It still cannot enter a running generator's radius: a noise made inside the light
/// is followed only as far as the boundary, where it waits. That is the whole lesson
/// of the first night, and nothing has to say it out loud.
///
/// Movement is direct steering with a CharacterController rather than a NavMesh: the
/// level is flat and the controller slides along walls for free. Swap to NavMeshAgent
/// when levels get real geometry.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Monster : MonoBehaviour, INoiseListener
{
    [Header("Hearing")]
    [Tooltip("Nothing beyond this is ever heard, however loud.")]
    public float hearingRadius = 30f;
    [Tooltip("Multiplies how far every sound carries to THIS monster. 2 = twice the ears.")]
    [Range(0.1f, 3f)] public float hearingSensitivity = 1f;
    [Tooltip("Metres of error in a sound heard at the very edge of its carry. " +
             "A sound made right next to it is located exactly.")]
    public float positionError = 3f;

    [Header("Movement")]
    public float patrolSpeed = 1.6f;
    public float alertSpeed = 2.6f;
    [Tooltip("Still well under the player's 5 walk / 9 sprint: it must always be outrunnable.")]
    public float chaseSpeed = 3.6f;
    public float turnSpeed = 6f;
    public float gravity = -9.81f;
    [Tooltip("How close counts as having reached a destination.")]
    public float arriveDistance = 1.2f;

    [Header("Patrol")]
    [Tooltip("Walked in order. Leave empty to wander the roam circle instead.")]
    public Transform[] patrolPoints;
    [Tooltip("Off = pick the next point at random rather than walking the loop in order.")]
    public bool patrolInOrder = true;
    [Tooltip("Seconds spent standing at each patrol point, listening.")]
    public float patrolWaitTime = 2f;

    [Header("Patrol fallback (used when no patrol points are assigned)")]
    public Vector3 roamCenter = Vector3.zero;
    public float roamRadius = 45f;

    [Header("Investigate")]
    [Tooltip("Seconds spent searching at a noise before giving up and going back to patrol.")]
    public float investigateDuration = 6f;
    [Tooltip("Degrees per second it turns on the spot while searching.")]
    public float scanTurnSpeed = 70f;

    [Header("Aggression")]
    [Tooltip("Every heard sound adds to this; it decays with silence. Cross the " +
             "threshold and the monster switches from walking over to running over.")]
    public float agitationPerSound = 1f;
    public float agitationDecayPerSecond = 0.45f;
    public float chaseThreshold = 2.5f;
    public float maxAgitation = 5f;

    [Header("Attack")]
    [Tooltip("It hurts whatever it walks into. This is touch, not sight.")]
    public float attackRange = 1.7f;
    public float attackDamage = 25f;
    public float attackInterval = 1.2f;

    [Header("Safe zone")]
    [Tooltip("How far outside the protection radius it lingers while waiting.")]
    public float standOffPadding = 1.5f;

    [Header("Doors")]
    [Tooltip("How far ahead it feels for a closed door.")]
    public float doorProbeDistance = 1.6f;
    [Tooltip("Seconds spent working at a door before it gives. The rattle is the warning.")]
    public float doorForceTime = 1.4f;

    [Header("Debug")]
    [Tooltip("Draws hearing radius, destination and state in the Scene view. " +
             "Turn it off for a clean view; it costs nothing either way.")]
    public bool showDebug = true;

    private CharacterController controller;
    private MonsterState state = MonsterState.Patrol;
    private Vector3 destination;
    private Vector3 lastHeardPosition;
    private float lastHeardTime = -999f;
    private float agitation;
    private float investigateTimer;
    private float patrolWaitTimer;
    private int patrolIndex;
    private Vector3 roamPoint;
    private float verticalVelocity;
    private float nextAttackTime;
    private DoorInteraction forcingDoor;
    private float forcingProgress;

    // Shared scratch: attacks are found by touch, so no monster needs a player reference.
    private static readonly Collider[] touchBuffer = new Collider[8];

    public MonsterState State { get { return state; } }
    public float Agitation { get { return agitation; } }
    public Vector3 Destination { get { return destination; } }
    public Vector3 LastHeardPosition { get { return lastHeardPosition; } }
    public bool HasHeardSomething { get { return lastHeardTime > 0f; } }

    /// <summary>True while it is working at a door rather than moving.</summary>
    public bool IsForcingDoor { get { return forcingDoor != null; } }

    /// <summary>True while it is heading for a noise rather than patrolling.</summary>
    public bool IsPursuing { get { return state == MonsterState.Alerted || state == MonsterState.Chase; } }

    /// <summary>
    /// Kept so MonsterSpawner does not have to know which kind of monster it spawned.
    /// Deliberately ignored: this one is blind and must never hold a player reference.
    /// </summary>
    public void SetTarget(Transform player) { }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        Noise.Register(this);
    }

    void OnDisable()
    {
        Noise.Unregister(this);
    }

    void Start()
    {
        PickRoamPoint();
        destination = NextPatrolDestination();
    }

    // ---------------------------------------------------------------- hearing

    /// <summary>
    /// The only way anything gets in. A sound is heard if it carries far enough for
    /// these ears and is inside hearingRadius; the position handed over is blurred by
    /// how faint it was, so a distant noise is a direction rather than a fix.
    /// </summary>
    public void OnNoiseHeard(NoiseEvent noise)
    {
        if (noise.source == gameObject) return;   // never react to itself

        float distance = FlatDistance(transform.position, noise.position);
        if (distance > hearingRadius) return;

        float carry = noise.radius * hearingSensitivity;
        if (distance > carry) return;

        // 1 right on top of it, 0 at the very limit of what carries this far.
        float clarity = carry <= 0.01f ? 1f : Mathf.Clamp01(1f - distance / carry);

        Vector2 blur = Random.insideUnitCircle * (positionError * (1f - clarity));
        Vector3 guess = noise.position + new Vector3(blur.x, 0f, blur.y);

        lastHeardPosition = KeepOutOfSafeZone(guess);
        lastHeardTime = Time.time;

        // A clear sound rattles it more than a faint one, and repeated sounds stack.
        agitation = Mathf.Min(maxAgitation, agitation + agitationPerSound * (0.5f + clarity));

        state = agitation >= chaseThreshold ? MonsterState.Chase : MonsterState.Alerted;
        destination = lastHeardPosition;
    }

    // ---------------------------------------------------------------- think

    void Update()
    {
        agitation = Mathf.Max(0f, agitation - agitationDecayPerSecond * Time.deltaTime);

        // A closed door in the way is worked at, not walked around. This is only
        // reachable when the generator is off: while it runs it cannot get near the
        // house at all, so "they can open doors" is really "when the light dies".
        if (TryForceDoor()) return;

        switch (state)
        {
            case MonsterState.Patrol: TickPatrol(); break;
            case MonsterState.Alerted: TickPursue(alertSpeed); break;
            case MonsterState.Chase: TickPursue(chaseSpeed); break;
            case MonsterState.Investigate: TickInvestigate(); break;
        }

        TryAttack();

        if (showDebug) Debug.DrawLine(transform.position + Vector3.up, destination + Vector3.up, StateColor());
    }

    private void TickPatrol()
    {
        if (patrolWaitTimer > 0f)
        {
            patrolWaitTimer -= Time.deltaTime;
            Move(Vector3.zero);
            Scan();
            return;
        }

        if (MoveTowards(destination, patrolSpeed)) return;

        patrolWaitTimer = patrolWaitTime;
        AdvancePatrol();
        destination = NextPatrolDestination();
    }

    private void TickPursue(float speed)
    {
        // Cooled off on the way over: finish the trip at a walk.
        if (state == MonsterState.Chase && agitation < chaseThreshold) state = MonsterState.Alerted;

        // The noise may have come from inside the light, or the light may have come on
        // since it was heard. Either way it can only get as far as the boundary.
        destination = KeepOutOfSafeZone(lastHeardPosition);

        if (MoveTowards(destination, speed)) return;

        state = MonsterState.Investigate;
        investigateTimer = investigateDuration;
    }

    private void TickInvestigate()
    {
        Move(Vector3.zero);
        Scan();

        investigateTimer -= Time.deltaTime;
        if (investigateTimer > 0f) return;

        // Heard nothing more. Back to the round, and calm down for real.
        agitation = 0f;
        state = MonsterState.Patrol;
        patrolWaitTimer = 0f;
        destination = NextPatrolDestination();
    }

    /// <summary>Turn on the spot, listening. The blind equivalent of looking around.</summary>
    private void Scan()
    {
        transform.Rotate(0f, scanTurnSpeed * Time.deltaTime, 0f, Space.Self);
    }

    // ---------------------------------------------------------------- patrol route

    private Vector3 NextPatrolDestination()
    {
        if (HasPatrolRoute())
        {
            Transform point = patrolPoints[Mathf.Clamp(patrolIndex, 0, patrolPoints.Length - 1)];
            if (point != null) return KeepOutOfSafeZone(point.position);
        }

        return roamPoint;
    }

    private void AdvancePatrol()
    {
        if (HasPatrolRoute())
        {
            if (patrolInOrder) patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            else patrolIndex = Random.Range(0, patrolPoints.Length);
            return;
        }

        PickRoamPoint();
    }

    private bool HasPatrolRoute()
    {
        return patrolPoints != null && patrolPoints.Length > 0;
    }

    /// <summary>
    /// Hand a shared route to a spawned monster. The start index is per-monster so a
    /// wave does not walk the loop in single file.
    /// </summary>
    public void SetPatrolRoute(Transform[] points, int startIndex)
    {
        patrolPoints = points;
        patrolIndex = (points == null || points.Length == 0)
                    ? 0
                    : ((startIndex % points.Length) + points.Length) % points.Length;
        destination = NextPatrolDestination();
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

    // ---------------------------------------------------------------- doors

    /// <summary>
    /// Feel ahead for a shut door and lean on it. Returns true while occupied with one,
    /// so the monster stands at the door instead of grinding into it.
    /// </summary>
    private bool TryForceDoor()
    {
        if (!IsPursuing)
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

            Move(Vector3.zero);   // hold position against the door while working at it
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

    // ---------------------------------------------------------------- moving

    /// <summary>Walk towards a point. Returns false once it has arrived.</summary>
    private bool MoveTowards(Vector3 point, float speed)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;

        if (flat.magnitude <= arriveDistance)
        {
            Move(Vector3.zero);
            return false;
        }

        Vector3 dir = flat.normalized;
        Move(dir * speed);
        Face(dir);
        return true;
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
            Vector3 edge = BoundaryPoint(here, transform.position);
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

    // ---------------------------------------------------------------- safe zone

    /// <summary>
    /// A point it is allowed to stand on. If the given one is inside a running
    /// generator's light, the nearest spot on the boundary is returned instead -- so a
    /// noise made in the house draws it to the edge of the light, never into it.
    /// </summary>
    private Vector3 KeepOutOfSafeZone(Vector3 point)
    {
        Generator protector = Generator.GetProtector(point);
        return protector == null ? point : BoundaryPoint(protector, point);
    }

    private Vector3 BoundaryPoint(Generator protector, Vector3 towards)
    {
        Vector3 centre = protector.transform.position;
        Vector3 outward = towards - centre;
        outward.y = 0f;

        if (outward.sqrMagnitude < 0.01f)
        {
            // Dead centre: come at it from wherever the monster already is.
            outward = transform.position - centre;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
        }

        Vector3 edge = centre + outward.normalized * (protector.protectionRadius + standOffPadding);
        return new Vector3(edge.x, towards.y, edge.z);
    }

    // ---------------------------------------------------------------- attack

    /// <summary>
    /// Hurts whatever it is touching. Found by overlap rather than by asking where the
    /// player is: it can only ever have got here by following a sound.
    /// </summary>
    private void TryAttack()
    {
        if (Time.time < nextAttackTime) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, attackRange, touchBuffer,
                                                  ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            PlayerVitals vitals = touchBuffer[i].GetComponentInParent<PlayerVitals>();
            if (vitals == null || !vitals.IsAlive) continue;
            if (Generator.IsPointProtected(vitals.transform.position)) continue;   // untouchable in the light

            vitals.TakeDamage(attackDamage);
            nextAttackTime = Time.time + attackInterval;
            return;
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------------------------------------------------------------- debug

    private Color StateColor()
    {
        switch (state)
        {
            case MonsterState.Chase: return new Color(1f, 0.25f, 0.2f);
            case MonsterState.Alerted: return new Color(1f, 0.65f, 0.15f);
            case MonsterState.Investigate: return new Color(1f, 0.95f, 0.3f);
            default: return new Color(0.5f, 0.8f, 1f);
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebug) return;

        // What it can hear.
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        // Where it is going, in the colour of the state it is in.
        Gizmos.color = StateColor();
        Gizmos.DrawLine(transform.position + Vector3.up, destination + Vector3.up);
        Gizmos.DrawWireCube(destination + Vector3.up * 0.1f, new Vector3(0.6f, 0.2f, 0.6f));

        // The last thing it heard.
        if (HasHeardSomething)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(lastHeardPosition + Vector3.up * 0.5f, 0.7f);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.color = StateColor();
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.4f,
                                  string.Format("{0}  agitation {1:0.0}", state, agitation));
#endif
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebug || !HasPatrolRoute()) return;

        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.8f);
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            Transform a = patrolPoints[i];
            Transform b = patrolPoints[(i + 1) % patrolPoints.Length];
            if (a == null) continue;

            Gizmos.DrawWireSphere(a.position, 0.5f);
            if (b != null) Gizmos.DrawLine(a.position, b.position);
        }
    }
}
