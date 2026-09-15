using UnityEngine;

/// <summary>Where the monster's head is at, in one word.</summary>
public enum MonsterState
{
    /// <summary>Walking its round, hearing nothing.</summary>
    Patrol,
    /// <summary>Heard something and is walking to where it thinks it came from.</summary>
    Alerted,
    /// <summary>Standing still at a spot, turning, listening for another noise.</summary>
    Investigate,
    /// <summary>Working its way round the noise, checking a few places near it in turn.</summary>
    Search,
    /// <summary>Heard enough, close enough, often enough. Moves fast.</summary>
    Chase,
    /// <summary>Nothing has its attention, so it has gone to try the house.</summary>
    Prowl,
    /// <summary>Standing in a generator's light, walking itself back out of it.</summary>
    Leaving
}

/// <summary>
/// A night creature that is BLIND. It has no vision and never reads the player's
/// transform -- everything it knows arrives through <see cref="Noise"/> as a position
/// and a loudness, blurred by distance. Stand still and make no sound and it walks
/// straight past you. A flashlight changes nothing.
///
/// It still cannot enter a running generator's radius: a noise made inside the light
/// is followed only as far as the boundary, where it waits. That is the whole lesson
/// of the first night, and nothing has to say it out loud. Switch the generator on
/// around one that is already indoors and it walks itself out through the door rather
/// than vanishing to the boundary -- see TickLeaving.
///
/// Every ten to three hundred seconds, on its own roll, it goes and tries the house
/// whether or not it has heard anything (see TickProwl). That is still not detection:
/// it has no idea whether anyone is in there. It is what makes the light worth keeping
/// lit before something is already after you.
///
/// Movement is still its own: a CharacterController it drives itself, so gravity, the
/// safe-zone push-out and attack-by-touch all stay here. Only the *route* comes from the
/// baked NavMesh, via <see cref="NavPathFollower"/> -- enough to round a corner without
/// handing steering over to a NavMeshAgent.
///
/// Where it goes when nothing has its attention is not decided here at all. That is
/// <see cref="MonsterPatrol"/>, which knows nothing about hearing and is reusable by a
/// monster that detects some other way.
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
    public float positionError = 5f;

    [Tooltip("The loudness a plain walking step carries (NoiseEmitter.walkNoiseRadius). Every noise " +
             "is weighed against this, so a crouched step counts for almost nothing and a sprint " +
             "counts for a great deal.")]
    public float referenceNoiseRadius = 8f;

    [Tooltip("Above 1 this widens the gap between the gaits: a sprint is not merely louder than a " +
             "walk, it is a different kind of event. Retune with chaseThreshold.")]
    [Range(1f, 3f)] public float noiseWeightExponent = 1.5f;

    [Tooltip("Ceiling on what one noise can be worth, so a very loud world noise (Tom's case at 30 m) " +
             "is a strong lure rather than an instant hunt.")]
    public float maxNoiseWeight = 3f;

    [Tooltip("While already on its way to a noise, it will not swing onto a newer one more often " +
             "than this. Louder noises still stack agitation -- it just commits to a direction " +
             "instead of twitching at every footstep.")]
    public float retargetCooldown = 0.4f;

    [Header("Movement")]
    public float patrolSpeed = 1.6f;
    public float alertSpeed = 2.6f;
    [Tooltip("Just over the player's 3.5 walk, under their 6 sprint: only stamina outruns it, and " +
             "only for a while. Retune with PlayerMovement.speed, sprintSpeed and staminaRegenRate.")]
    public float chaseSpeed = 4f;
    public float turnSpeed = 6f;
    public float gravity = -9.81f;
    [Tooltip("How close counts as having reached a destination.")]
    public float arriveDistance = 1.2f;

    [Header("Patrol")]
    [Tooltip("Optional places worth visiting; MonsterSpawner fills this from PatrolRoute. " +
             "They only bias the roam -- they are not a loop, see MonsterPatrol.anchorChance.")]
    public Transform[] patrolPoints;
    [Tooltip("Seconds of barely moving before it writes the current leg off and picks another.")]
    public float patrolStuckTime = 1.5f;
    [Tooltip("Seconds allowed per metre of the leg before it is abandoned. Catches the slow " +
             "wedges that never quite stop moving, which a stuck timer alone would never see.")]
    public float patrolSecondsPerMetre = 1.6f;

    [Header("Patrol area (handed to MonsterPatrol on Start)")]
    public Vector3 roamCenter = Vector3.zero;
    public float roamRadius = 45f;

    [Header("Investigate and search")]
    [Tooltip("Seconds spent standing and listening at one spot. The whole search is this several " +
             "times over, with a walk to somewhere new in between.")]
    public float investigateDuration = 3f;

    [Tooltip("Fewest places near the noise it checks before giving up on it.")]
    public int minSearchPoints = 2;
    [Tooltip("Most places near the noise it checks. Two to four is a search you can watch " +
             "happening; more reads as the monster knowing something it does not.")]
    public int maxSearchPoints = 4;

    [Tooltip("How far from the heard position a search may wander. Roughly how wrong the blurred " +
             "position can be, so the real spot is usually somewhere inside the sweep.")]
    public float searchRadius = 10f;

    [Tooltip("Seconds it keeps its round near a place that went quiet on it, after the search " +
             "itself fails. The leash widens back out over this time -- see MonsterPatrol.Bias. " +
             "This is what stops the player waiting a moment and walking off unbothered.")]
    public float searchInterestTime = 38f;

    [Tooltip("How wide the round is held while that interest lasts.")]
    public float searchInterestRadius = 18f;
    [Tooltip("Degrees per second it turns while looking around.")]
    public float scanTurnSpeed = 70f;
    [Tooltip("Seconds it holds each direction before turning to a new one. Standing and " +
             "listening reads as searching; revolving on the spot reads as a bug.")]
    public float scanHoldTime = 0.9f;
    [Tooltip("How far either side of where it is facing a look may go.")]
    public float scanSweepAngle = 110f;

    [Header("Aggression")]
    [Tooltip("What one noise of reference loudness and middling clarity adds. Louder noises are " +
             "worth more (see noiseWeightExponent); it all decays with silence.")]
    public float agitationPerSound = 1f;
    public float agitationDecayPerSecond = 0.45f;
    [Tooltip("Cross this and it runs instead of walking over. At the defaults: creeping never gets " +
             "here, walking in earshot takes several seconds, sprinting takes two steps.")]
    public float chaseThreshold = 4f;
    [Tooltip("Headroom above the threshold, so a chase carries on for a few seconds of silence " +
             "rather than dropping to a walk the instant you stop.")]
    public float maxAgitation = 6f;

    [Header("Attack")]
    [Tooltip("It hurts whatever it walks into. This is touch, not sight.")]
    public float attackRange = 1.7f;
    public float attackDamage = 25f;
    public float attackInterval = 1.2f;

    [Header("Prowling the house")]
    [Tooltip("Shortest wait before it goes and tries the house.")]
    public float minProwlInterval = 10f;
    [Tooltip("Longest wait. Rolled fresh every time and per monster, so the wave never arrives together.")]
    public float maxProwlInterval = 300f;
    [Tooltip("How far from the house centre the spot it makes for may be. Small enough that the spot " +
             "is usually a room rather than the yard.")]
    public float prowlRadius = 5f;
    [Tooltip("Seconds it stands inside, looking about, before going back to its round.")]
    public float prowlLinger = 10f;
    [Tooltip("The house it tries. Left empty it finds the House root itself, and failing that uses " +
             "the origin -- which is where the house is built.")]
    public Transform house;

    [Header("Safe zone")]
    [Tooltip("How far outside the protection radius it lingers while waiting.")]
    public float standOffPadding = 1.5f;
    [Tooltip("Seconds it is given to walk itself out of a light that came on around it before it is " +
             "placed on the boundary instead. The walk is the behaviour; this is only the backstop " +
             "for a monster with no route out, so \"never inside the light\" still holds.")]
    public float leaveGraceSeconds = 8f;

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
    private MonsterPatrol patrol;
    private readonly NavPathFollower follower = new NavPathFollower();
    private MonsterState state = MonsterState.Patrol;
    private Vector3 destination;
    private Vector3 lastHeardPosition;
    private float lastHeardTime = -999f;
    private float agitation;
    private float lastRetargetTime = -999f;
    private float investigateTimer;
    private Vector3 searchCentre;
    private Vector3 searchPoint;
    private int searchesLeft;
    private float searchLegTimer;
    private float searchLegAllowance;
    private Quaternion scanTarget;
    private float scanHoldTimer;
    private float patrolWaitTimer;
    private float patrolStuckTimer;
    private float patrolLegTimer;
    private float patrolLegAllowance;
    private Vector3 houseCentre;
    private float prowlTimer;
    private Vector3 prowlPoint;
    private float prowlLingerTimer;
    private float prowlLegTimer;
    private float prowlLegAllowance;
    private MonsterState stateBeforeLeaving = MonsterState.Patrol;
    private float leavingTimer;
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

    /// <summary>Seconds until it next goes and tries the house. Readouts only.</summary>
    public float ProwlTimeLeft { get { return Mathf.Max(0f, prowlTimer); } }

    /// <summary>True while it is heading for a noise rather than patrolling.</summary>
    public bool IsPursuing { get { return state == MonsterState.Alerted || state == MonsterState.Chase; } }

    /// <summary>True while it is working through the places near a noise it lost.</summary>
    public bool IsSearching { get { return state == MonsterState.Search || state == MonsterState.Investigate; } }

    /// <summary>Places near the noise it still means to check. Readouts only.</summary>
    public int SearchesLeft { get { return searchesLeft; } }

    /// <summary>Seconds it will keep its round near the last place that interested it.</summary>
    public float InterestTimeLeft { get { return patrol == null ? 0f : patrol.BiasTimeLeft; } }

    /// <summary>
    /// Kept so MonsterSpawner does not have to know which kind of monster it spawned.
    /// Deliberately ignored: this one is blind and must never hold a player reference.
    /// </summary>
    public void SetTarget(Transform player) { }

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Added rather than required, so an existing prefab keeps working untouched.
        patrol = GetComponent<MonsterPatrol>();
        if (patrol == null) patrol = gameObject.AddComponent<MonsterPatrol>();
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
        // Start, not Awake: MonsterSpawner writes these straight after Instantiate, which is
        // already too late for Awake.
        patrol.areaCenter = roamCenter;
        patrol.areaRadius = roamRadius;
        if (patrolPoints != null && patrolPoints.Length > 0) patrol.anchors = patrolPoints;

        houseCentre = house != null ? house.position : FindHouse();
        ScheduleProwl();

        BeginPatrolLeg();
    }

    // ---------------------------------------------------------------- hearing

    /// <summary>
    /// The only way anything gets in. A sound is heard if it carries far enough for
    /// these ears and is inside hearingRadius; the position handed over is blurred by
    /// how faint it was, so a distant noise is a direction rather than a fix.
    ///
    /// How hard it reacts is the sound's own loudness weighed against a walking step, so
    /// the three gaits are three different events rather than the same one at three
    /// ranges: creeping is barely worth noticing, walking brings it over to look, and a
    /// sprint is worth several walks at once and tips it straight into a chase.
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

        // A clear sound rattles it more than a faint one, a loud one more than a quiet
        // one, and repeated sounds stack.
        agitation = Mathf.Min(maxAgitation,
                              agitation + agitationPerSound * NoiseWeight(noise.radius) * (0.5f + clarity));

        // Walk or run, decided by agitation alone -- before the cooldown below, because that
        // governs only *where* it is going. Gating this on it too meant a second sprinting
        // step could pile on agitation and still never tip into a chase.
        // Walking out of a light is not interruptible -- it has to be out before it can go
        // anywhere -- so what it hears meanwhile becomes the state it leaves in instead.
        if (state == MonsterState.Leaving)
        {
            stateBeforeLeaving = agitation >= chaseThreshold ? MonsterState.Chase : MonsterState.Alerted;
        }
        else if (agitation >= chaseThreshold) state = MonsterState.Chase;
        else if (!IsPursuing) state = MonsterState.Alerted;

        // Already on its way somewhere: hold that direction for a moment rather than swinging
        // onto every footfall. The agitation above still landed, so a louder noise still tells.
        if (IsPursuing && Time.time - lastRetargetTime < retargetCooldown) return;

        Vector2 blur = Random.insideUnitCircle * (positionError * (1f - clarity));
        Vector3 guess = noise.position + new Vector3(blur.x, 0f, blur.y);

        lastHeardPosition = KeepOutOfSafeZone(guess);
        lastHeardTime = Time.time;
        lastRetargetTime = Time.time;
        destination = lastHeardPosition;
    }

    /// <summary>
    /// What a noise of this loudness is worth, as a multiple of a plain walking step.
    /// Above a linear curve on purpose: sprinting has to be a different decision from
    /// walking, not the same one sooner.
    /// </summary>
    public float NoiseWeight(float radius)
    {
        if (referenceNoiseRadius <= 0.01f) return 1f;
        return Mathf.Min(maxNoiseWeight, Mathf.Pow(radius / referenceNoiseRadius, noiseWeightExponent));
    }

    // ---------------------------------------------------------------- think

    void Update()
    {
        agitation = Mathf.Max(0f, agitation - agitationDecayPerSecond * Time.deltaTime);
        prowlTimer -= Time.deltaTime;

        // A light that came on around it is walked out of, never teleported out of. This runs
        // before anything else picks a destination: while it is standing in the protection
        // there is exactly one thing it is allowed to be doing.
        if (TickLeaving())
        {
            TryAttack();   // costs nothing: a player inside the light is untouchable anyway
            return;
        }

        // A closed door in the way is worked at, not walked around. Hunting or prowling,
        // this is only reachable when the generator is off -- while it runs it cannot get
        // near the house at all -- so "they can open doors" is really "when the light dies".
        if (TryForceDoor()) return;

        switch (state)
        {
            case MonsterState.Patrol: TickPatrol(); break;
            case MonsterState.Alerted: TickPursue(alertSpeed); break;
            case MonsterState.Chase: TickPursue(chaseSpeed); break;
            case MonsterState.Investigate: TickInvestigate(); break;
            case MonsterState.Search: TickSearch(); break;
            case MonsterState.Prowl: TickProwl(); break;
        }

        TryAttack();

        if (showDebug) Debug.DrawLine(transform.position + Vector3.up, destination + Vector3.up, StateColor());
    }

    private void TickPatrol()
    {
        // Every so often it stops roaming and goes to try the house. Only ever from here:
        // anything that has its attention wins, and the roll simply waits its turn.
        if (prowlTimer <= 0f && BeginProwl()) return;

        if (patrolWaitTimer > 0f)
        {
            patrolWaitTimer -= Time.deltaTime;
            Move(Vector3.zero);
            return;   // stands still, still facing the way it was walking. Nothing turns on the spot.
        }

        if (!patrol.HasDestination)
        {
            BeginPatrolLeg();
            return;
        }

        // The generator may have started since this leg was chosen; the light is out of bounds
        // whether or not it was when it set off.
        if (Generator.IsPointProtected(patrol.Destination) || PatrolLegLost())
        {
            BeginPatrolLeg();
            return;
        }

        destination = patrol.Destination;
        if (MoveTowards(destination, patrolSpeed))
        {
            patrolLegTimer += Time.deltaTime;
            return;
        }

        // Arrived. Usually straight on to the next leg; occasionally a short breather.
        patrol.Forget();
        follower.Clear();
        patrolWaitTimer = patrol.NextPauseTime();
    }

    /// <summary>Take a new destination and reset everything that judges the trip.</summary>
    private void BeginPatrolLeg()
    {
        follower.Clear();
        patrolStuckTimer = 0f;
        patrolLegTimer = 0f;

        if (!patrol.ChooseDestination(transform.position))
        {
            // Nowhere legal right now -- usually boxed in by the light. Back off a moment
            // rather than running twenty pathfinds every frame for the rest of the night.
            Move(Vector3.zero);
            patrolWaitTimer = 0.25f;
            return;
        }

        destination = patrol.Destination;
        patrolLegAllowance = FlatDistance(transform.position, destination) * patrolSecondsPerMetre + 4f;
    }

    /// <summary>
    /// True when this leg is not going to happen: no route to it, wedged against something,
    /// or simply taking far longer than the distance can explain. Any of the three means
    /// pick somewhere else, rather than lean on a wall for the rest of the night.
    /// </summary>
    private bool PatrolLegLost()
    {
        if (follower.Blocked) return true;

        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        if (velocity.magnitude < patrolSpeed * 0.3f) patrolStuckTimer += Time.deltaTime;
        else patrolStuckTimer = 0f;

        return patrolStuckTimer >= patrolStuckTime || patrolLegTimer >= patrolLegAllowance;
    }

    private void TickPursue(float speed)
    {
        // Walk or run is agitation's decision every frame, both ways: cooled off on the way
        // over and it finishes the trip at a walk, rattled again and it breaks into a run
        // without waiting for the next noise to re-decide it.
        state = agitation >= chaseThreshold ? MonsterState.Chase : MonsterState.Alerted;

        // The noise may have come from inside the light, or the light may have come on
        // since it was heard. Either way it can only get as far as the boundary.
        destination = KeepOutOfSafeZone(lastHeardPosition);

        if (MoveTowards(destination, speed)) return;

        // Arrived where the sound seemed to come from, and it is not there. Start a search
        // of the area rather than one long stare at an empty patch of ground.
        searchCentre = lastHeardPosition;
        searchesLeft = Random.Range(minSearchPoints, maxSearchPoints + 1);
        BeginListening();
    }

    /// <summary>Stand still, look about, and let whatever comes next be decided on the way out.</summary>
    private void BeginListening()
    {
        state = MonsterState.Investigate;
        investigateTimer = investigateDuration;
        scanHoldTimer = 0f;   // look somewhere immediately rather than after the first hold
        follower.Clear();
    }

    private void TickInvestigate()
    {
        Move(Vector3.zero);
        Scan();

        investigateTimer -= Time.deltaTime;
        if (investigateTimer > 0f) return;

        // Nothing here. Somewhere else nearby, while there is anywhere left to try.
        if (searchesLeft > 0 &&
            patrol.ChooseNear(searchCentre, searchRadius, transform.position, out searchPoint))
        {
            searchesLeft--;
            state = MonsterState.Search;
            destination = searchPoint;
            follower.Clear();

            // Same guard the patrol legs have: a step that is taking far longer than its
            // length can explain is written off rather than leaned on all night.
            searchLegTimer = 0f;
            searchLegAllowance = FlatDistance(transform.position, searchPoint) * patrolSecondsPerMetre + 4f;
            return;
        }

        GiveUpSearch();
    }

    private void TickSearch()
    {
        // The light may have come on while it was working the area over.
        destination = KeepOutOfSafeZone(searchPoint);

        searchLegTimer += Time.deltaTime;
        bool lost = follower.Blocked || searchLegTimer >= searchLegAllowance;

        if (!lost && MoveTowards(destination, alertSpeed)) return;

        BeginListening();   // arrived, or gave up on getting there: listen here either way
    }

    /// <summary>
    /// The search found nothing. It calms down, but it does not forget where it was: the
    /// round is held near the area for a while and only widens back out to the whole map
    /// as that interest fades. So a noise costs the player the area, not six seconds.
    /// </summary>
    private void GiveUpSearch()
    {
        agitation = 0f;
        searchesLeft = 0;

        patrol.Bias(searchCentre, searchInterestRadius, searchInterestTime);

        state = MonsterState.Patrol;
        patrolWaitTimer = 0f;
        patrol.Forget();       // a fresh destination, so it walks on rather than back
        follower.Clear();
    }

    /// <summary>
    /// Look around: turn to a direction, hold it, then choose another. Deliberately not a
    /// continuous spin -- searching has to read as searching, and a monster revolving on
    /// the spot reads as a bug. Only ever runs while investigating.
    /// </summary>
    private void Scan()
    {
        scanHoldTimer -= Time.deltaTime;
        if (scanHoldTimer <= 0f)
        {
            float yaw = transform.eulerAngles.y + Random.Range(-scanSweepAngle, scanSweepAngle);
            scanTarget = Quaternion.Euler(0f, yaw, 0f);

            // Hold *after* arriving, so a wide turn does not eat its own pause.
            scanHoldTimer = scanHoldTime +
                            Quaternion.Angle(transform.rotation, scanTarget) / Mathf.Max(1f, scanTurnSpeed);
        }

        transform.rotation = Quaternion.RotateTowards(transform.rotation, scanTarget,
                                                      scanTurnSpeed * Time.deltaTime);
    }

    // ---------------------------------------------------------------- prowling

    /// <summary>
    /// Where the house is. The origin is the fallback because that is where the prototype
    /// house is built, so a monster with nothing wired still prowls somewhere sensible.
    /// </summary>
    private Vector3 FindHouse()
    {
        GameObject root = GameObject.Find("House");
        return root == null ? Vector3.zero : root.transform.position;
    }

    /// <summary>
    /// Roll the wait before the next visit. Rolled per monster and re-rolled after every
    /// attempt, so four monsters never fall into step with each other.
    /// </summary>
    private void ScheduleProwl()
    {
        prowlTimer = Random.Range(minProwlInterval, maxProwlInterval);
    }

    /// <summary>
    /// Go and try the house. This is not detection and not a lure -- it still has no idea
    /// whether anyone is in there. It simply works its way indoors every so often, which is
    /// what makes keeping the generator lit worth doing before anything is after you.
    ///
    /// Returns false when the house cannot be got into at all right now, which is almost
    /// always because the generator is running: ChooseNear refuses anywhere inside the light.
    /// So "the light keeps them out" needs no code of its own here.
    /// </summary>
    private bool BeginProwl()
    {
        if (!patrol.ChooseNear(houseCentre, prowlRadius, transform.position, out prowlPoint))
        {
            ScheduleProwl();
            return false;
        }

        state = MonsterState.Prowl;
        destination = prowlPoint;
        prowlLingerTimer = 0f;
        prowlLegTimer = 0f;

        // The same lost-leg guard a patrol leg gets: a walk taking far longer than its length
        // can explain is written off rather than leaned on.
        prowlLegAllowance = FlatDistance(transform.position, prowlPoint) * patrolSecondsPerMetre + 4f;
        follower.Clear();
        return true;
    }

    private void TickProwl()
    {
        // Inside, having a look round before going back to its round.
        if (prowlLingerTimer > 0f)
        {
            Move(Vector3.zero);
            Scan();

            prowlLingerTimer -= Time.deltaTime;
            if (prowlLingerTimer <= 0f) EndProwl();
            return;
        }

        // Someone started the generator while it was on its way over: not welcome after all.
        if (Generator.IsPointProtected(prowlPoint))
        {
            EndProwl();
            return;
        }

        prowlLegTimer += Time.deltaTime;
        if (follower.Blocked || prowlLegTimer >= prowlLegAllowance)
        {
            EndProwl();
            return;
        }

        // A walk, not a hunt: it is going to have a look, and the player only learns it is
        // coming from the footsteps and the door.
        if (MoveTowards(prowlPoint, patrolSpeed)) return;

        prowlLingerTimer = prowlLinger;
        scanHoldTimer = 0f;   // look about at once rather than after the first hold
    }

    private void EndProwl()
    {
        ScheduleProwl();

        state = MonsterState.Patrol;
        patrolWaitTimer = 0f;
        patrol.Forget();       // a fresh leg from where it now stands
        follower.Clear();
    }

    // ---------------------------------------------------------------- leaving the light

    /// <summary>
    /// Keeps the promise that nothing stands in a running generator's light -- by walking it
    /// out, which is the one thing snapping it to the boundary could never do. Switch the
    /// generator on with a monster in the kitchen and it turns round and walks out of the
    /// door it came in by, forcing it again if someone shut it.
    ///
    /// Returns true while it is on its way out, and nothing else may run that frame.
    /// </summary>
    private bool TickLeaving()
    {
        Generator here = Generator.GetProtector(transform.position);

        if (here == null)
        {
            if (state == MonsterState.Leaving) StopLeaving();
            return false;
        }

        if (state != MonsterState.Leaving) BeginLeaving();

        leavingTimer += Time.deltaTime;

        // The backstop, not the behaviour: a monster with no route out -- shut in by a door it
        // cannot force, wedged in a corner -- would otherwise stand in the light all night.
        if (leavingTimer >= leaveGraceSeconds)
        {
            PlaceOnBoundary(here);
            StopLeaving();
            return true;
        }

        // Doors are worked at on the way out too: it may well have been shut in.
        if (TryForceDoor()) return true;

        destination = BoundaryPoint(here, transform.position);

        // No route out is still no reason to stand still: straight at the nearest edge.
        if (follower.Blocked)
        {
            Vector3 straight = destination - transform.position;
            straight.y = 0f;
            if (straight.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = straight.normalized;
                Move(dir * alertSpeed);
                Face(dir);
            }
            else Move(Vector3.zero);
        }
        else MoveTowards(destination, alertSpeed);

        return true;
    }

    private void BeginLeaving()
    {
        // A prowl into a house that has just been lit is void. Anything else it was doing is
        // picked up where it left off, since every other state re-derives its destination
        // every frame anyway -- and all of them keep out of the light on their own.
        if (state == MonsterState.Prowl)
        {
            stateBeforeLeaving = MonsterState.Patrol;
            ScheduleProwl();
        }
        else stateBeforeLeaving = state;

        state = MonsterState.Leaving;
        leavingTimer = 0f;
        forcingDoor = null;
        follower.Clear();
    }

    private void StopLeaving()
    {
        state = stateBeforeLeaving;
        leavingTimer = 0f;
        follower.Clear();

        if (state == MonsterState.Patrol)
        {
            patrol.Forget();
            patrolWaitTimer = 0f;
        }
    }

    /// <summary>The old hard guarantee, kept only for a monster that could not walk itself out.</summary>
    private void PlaceOnBoundary(Generator protector)
    {
        Vector3 edge = BoundaryPoint(protector, transform.position);
        controller.enabled = false;
        transform.position = new Vector3(edge.x, transform.position.y, edge.z);
        controller.enabled = true;
    }

    // ---------------------------------------------------------------- patrol route

    /// <summary>
    /// Hand a spawned monster the level's places of interest. They become MonsterPatrol
    /// anchors -- somewhere it chooses more often than average, not a loop it walks -- so
    /// startIndex no longer means anything; it is kept so MonsterSpawner is unchanged.
    /// </summary>
    public void SetPatrolRoute(Transform[] points, int startIndex)
    {
        patrolPoints = points;
        if (patrol == null) patrol = GetComponent<MonsterPatrol>();
        if (patrol != null) patrol.anchors = points;
    }

    // ---------------------------------------------------------------- doors

    /// <summary>
    /// Feel ahead for a shut door and lean on it. Returns true while occupied with one,
    /// so the monster stands at the door instead of grinding into it.
    /// </summary>
    private bool TryForceDoor()
    {
        // Searching counts as hunting: the NavMesh is baked as though the doors were open,
        // so a search leg through a shut one would otherwise wedge against it and time out.
        // Prowling and leaving count too: a shut front door is the whole of "trying to get in",
        // and a monster shut in by a light that came on around it has to get back out.
        if (!IsPursuing && state != MonsterState.Search &&
            state != MonsterState.Prowl && state != MonsterState.Leaving)
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

    /// <summary>
    /// Walk towards a point, routed round geometry by the baked NavMesh. Returns false once
    /// it has arrived. With no mesh baked the follower hands the point straight back, which
    /// is exactly the direct steering this used to do.
    /// </summary>
    private bool MoveTowards(Vector3 point, float speed)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;

        if (flat.magnitude <= arriveDistance)
        {
            Move(Vector3.zero);
            return false;
        }

        Vector3 step = follower.Steer(transform.position, point) - transform.position;
        step.y = 0f;
        if (step.sqrMagnitude < 0.0001f) step = flat;   // standing on the next corner: aim past it

        Vector3 dir = step.normalized;
        Move(dir * speed);
        Face(dir);                                       // facing always follows travel, never a timer

        if (showDebug) follower.DrawDebug(transform.position, StateColor());
        return true;
    }

    private void Move(Vector3 horizontal)
    {
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontal + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        // Ending a frame inside a running generator's radius is deliberately not corrected
        // here. Whatever put it there -- the light coming on around it, or a collision slide --
        // TickLeaving picks up next frame and walks it out on its own legs.
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
            case MonsterState.Search: return new Color(0.85f, 1f, 0.4f);
            case MonsterState.Prowl: return new Color(0.8f, 0.5f, 1f);
            case MonsterState.Leaving: return new Color(0.6f, 1f, 0.85f);
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

        // The patch of ground it is working over, and how much of it is left to try.
        if (IsSearching)
        {
            Gizmos.color = new Color(1f, 0.95f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(searchCentre, searchRadius);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.color = StateColor();
        string extra = IsSearching ? string.Format("  {0} to check", searchesLeft)
                     : InterestTimeLeft > 0f ? string.Format("  interested {0:0}s", InterestTimeLeft)
                     : "";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.4f,
                                  string.Format("{0}  agitation {1:0.0}{2}", state, agitation, extra));
#endif
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebug || patrolPoints == null) return;

        // Spheres, not a loop: these are places it may head for, not an order it walks in.
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.8f);
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] != null) Gizmos.DrawWireSphere(patrolPoints[i].position, 0.5f);
        }
    }
}
