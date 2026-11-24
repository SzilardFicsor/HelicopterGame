using UnityEngine;

public class EnemyHelicopterAI : MonoBehaviour
{
    public enum Behaviour
    {
        Patrol,
        Roam,
        Chase,
        Orbit,
        Dogfight
    }

    public enum Maneuver
    {
        Approach,   // close the distance (with a little weaving)
        Strafe,     // slide left/right while facing the target
        Orbit,      // circle the target while facing it
        Evade,      // sharp sideways + up/down jink
        Retreat     // back away and gain some height
    }

    [System.Serializable]
    public struct AxisSettings
    {
        public float maxSpeed;
        public float acceleration;
        public float deceleration;

        public AxisSettings(float maxSpeed, float acceleration, float deceleration)
        {
            this.maxSpeed = maxSpeed;
            this.acceleration = acceleration;
            this.deceleration = deceleration;
        }
    }

    [System.Serializable]
    public struct ManeuverSettings
    {
        [Tooltip("Min/Max seconds the maneuver lasts before a new one is picked.")]
        public Vector2 duration;

        [Tooltip("Pick weight by health. X: 0 = almost dead, 1 = full health. Y: weight (higher = more likely).")]
        public AnimationCurve weightByHealth;

        public ManeuverSettings(float minDuration, float maxDuration, float weightLowHealth, float weightFullHealth)
        {
            duration = new Vector2(minDuration, maxDuration);
            weightByHealth = AnimationCurve.Linear(0f, weightLowHealth, 1f, weightFullHealth);
        }
    }

    // =========================================================
    // GENERAL
    // =========================================================

    [Header("Behaviour")]
    [Tooltip("Don't set this to Dogfight. It is entered automatically when the target is detected.")]
    [SerializeField] private Behaviour defaultBehaviour = Behaviour.Patrol;
    [SerializeField] private Transform target;

    [Tooltip("Automatically engage when the target gets close.")]
    [SerializeField] private bool detectTargetAutomatically = true;

    [Tooltip("When engaging, use the smart Dogfight behaviour instead of plain Chase.")]
    [SerializeField] private bool useDogfight = true;

    [SerializeField] private float detectionRange = 100f;
    [SerializeField] private float loseTargetRange = 140f;

    [Tooltip("Layers that block target detection.")]
    [SerializeField] private LayerMask targetObstructionMask;

    private Behaviour currentBehaviour;

    // =========================================================
    // MOVEMENT
    // =========================================================

    [Header("Movement - Horizontal")]
    [SerializeField] private AxisSettings forwardAxis = new AxisSettings(25f, 10f, 4f);
    [SerializeField] private AxisSettings backwardAxis = new AxisSettings(12f, 8f, 5f);
    [SerializeField] private AxisSettings rightAxis = new AxisSettings(14f, 8f, 4f);
    [SerializeField] private AxisSettings leftAxis = new AxisSettings(14f, 8f, 4f);

    [Header("Movement - Vertical")]
    [SerializeField] private AxisSettings upAxis = new AxisSettings(8f, 6f, 5f);
    [SerializeField] private AxisSettings downAxis = new AxisSettings(10f, 8f, 6f);

    [Header("Movement Feel")]
    [SerializeField] private float inputSmoothTime = 0.12f;
    [SerializeField] private float counterThrustMultiplier = 1f;
    [SerializeField] private float thrustDelay = 0.15f;
    [SerializeField] private float thrustEaseSpeed = 2f;

    [Header("Rotation")]
    [SerializeField] private float yawSmoothTime = 0.2f;
    [SerializeField] private float maxYawSpeed = 150f;

    [Tooltip("How much the helicopter turns toward its movement direction.")]
    [Range(0f, 1f)]
    [SerializeField] private float movementFacingWeight = 1f;

    // =========================================================
    // PATROL
    // =========================================================

    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private bool loopPatrol = true;
    [SerializeField] private float waypointReachDistance = 8f;
    [SerializeField] private float patrolAltitude = 20f;

    private int currentWaypoint;

    // =========================================================
    // ROAM
    // =========================================================

    [Header("Roaming")]
    [SerializeField] private float roamRadius = 40f;
    [SerializeField] private float roamAltitude = 20f;
    [SerializeField] private float roamReachDistance = 8f;
    [SerializeField] private float roamWaitTime = 2f;

    private Vector3 roamCenter;
    private Vector3 roamDestination;
    private float roamWaitTimer;

    // =========================================================
    // CHASE
    // =========================================================

    [Header("Chase")]
    [SerializeField] private float chaseDistance = 25f;

    [Tooltip("Height above the target that the helicopter tries to maintain.")]
    [SerializeField] private float chaseHeight = 10f;

    [Tooltip("Predicts target movement when chasing.")]
    [SerializeField] private float chasePrediction = 0.3f;

    // =========================================================
    // ORBIT / STRAFE (old standalone behaviours)
    // =========================================================

    [Header("Orbit (standalone Orbit behaviour)")]
    [SerializeField] private float orbitDistance = 30f;
    [SerializeField] private float orbitHeight = 15f;
    [SerializeField] private float orbitSpeed = 12f;
    [Tooltip("1 = clockwise, -1 = counterclockwise.")]
    [SerializeField] private float orbitDirection = 1f;
    [SerializeField] private float orbitDistanceCorrection = 1f;

    [Header("Strafing (Chase / Orbit behaviours)")]
    [SerializeField] private bool enableStrafing = true;
    [SerializeField] private float strafeStrength = 0.5f;
    [SerializeField] private float strafeFrequency = 0.5f;

    // =========================================================
    // DOGFIGHT
    // =========================================================

    [Header("Dogfight - Combat Space")]
    [Tooltip("Closer than this and the helicopter wants to back off.")]
    [SerializeField] private float minCombatDistance = 25f;

    [Tooltip("Farther than this and the helicopter wants to close in.")]
    [SerializeField] private float maxCombatDistance = 55f;

    [Tooltip("Distance over which the in/out correction ramps up to full speed.")]
    [SerializeField] private float distanceCorrectionBand = 10f;

    [Tooltip("Min/Max height above the target. A new height is picked for every maneuver.")]
    [SerializeField] private Vector2 combatHeightRange = new Vector2(5f, 18f);

    [SerializeField] private float heightCorrectionGain = 0.25f;

    [Header("Dogfight - Facing")]
    [Tooltip("Helicopter keeps facing the target during the dogfight so it can fire.")]
    [SerializeField] private float dogfightYawSpeed = 220f;
    [SerializeField] private float dogfightYawSmoothTime = 0.15f;

    [Tooltip("Seconds of target movement to lead the aim by.")]
    [SerializeField] private float aimPrediction = 0.2f;

    [Header("Dogfight - Maneuver Weights (X = health 0..1)")]
    [SerializeField] private ManeuverSettings approachManeuver = new ManeuverSettings(2f, 4f, 0.5f, 3f);
    [SerializeField] private ManeuverSettings strafeManeuver = new ManeuverSettings(2f, 4f, 2f, 4f);
    [SerializeField] private ManeuverSettings orbitManeuver = new ManeuverSettings(3f, 6f, 1f, 2f);
    [SerializeField] private ManeuverSettings evadeManeuver = new ManeuverSettings(0.8f, 1.5f, 3f, 1f);
    [SerializeField] private ManeuverSettings retreatManeuver = new ManeuverSettings(2f, 4f, 2.5f, 0.2f);

    [Header("Dogfight - Distance Bias")]
    [Tooltip("Approach weight multiplier when farther than max combat distance.")]
    [SerializeField] private float farApproachBonus = 4f;

    [Tooltip("Retreat/Evade weight multiplier when closer than min combat distance.")]
    [SerializeField] private float closeRetreatBonus = 4f;

    [Header("Dogfight - Maneuver Tuning")]
    [Range(0f, 1f)][SerializeField] private float strafeIntensity = 0.9f;
    [Tooltip("Min/Max seconds before a strafe flips direction.")]
    [SerializeField] private Vector2 strafeFlipInterval = new Vector2(0.8f, 2f);

    [Range(0f, 1f)][SerializeField] private float orbitIntensity = 0.8f;

    [Range(0f, 1f)][SerializeField] private float approachWeave = 0.3f;
    [SerializeField] private float approachWeaveFrequency = 0.4f;

    [SerializeField] private float evadeVertical = 0.8f;
    [SerializeField] private float evadeBackward = 0.3f;

    [Header("Dogfight - Reactions")]
    [Tooltip("Chance to jink immediately after taking damage.")]
    [Range(0f, 1f)][SerializeField] private float evadeOnDamageChance = 0.35f;
    [SerializeField] private float reactiveEvadeCooldown = 3f;

    private Maneuver currentManeuver;
    private float maneuverTimer;
    private float maneuverSide = 1f;
    private float maneuverHeight;
    private float evadeVerticalSign = 1f;
    private float sideFlipTimer;
    private float nextReactiveEvadeTime;

    // =========================================================
    // OBSTACLE AVOIDANCE
    // =========================================================

    [Header("Obstacle Avoidance")]
    [SerializeField] private bool avoidObstacles = true;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private float obstacleDetectionDistance = 25f;
    [SerializeField] private float obstacleAvoidanceStrength = 2f;
    [SerializeField] private float obstacleSphereRadius = 3f;
    [SerializeField] private float sideAvoidanceWeight = 1.5f;
    [SerializeField] private float avoidanceLookAhead = 20f;

    // =========================================================
    // VISUALS
    // =========================================================

    [Header("Visual Model")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private float maxPitchTilt = 16f;
    [SerializeField] private float maxBankTilt = 16f;
    [SerializeField] private float cruiseTilt = 5f;
    [SerializeField] private float tiltResponse = 6f;

    [Header("Hover")]
    [SerializeField] private float bobAmplitude = 0.12f;
    [SerializeField] private float bobFrequency = 0.6f;

    [Header("Rotors")]
    [SerializeField] private Transform mainRotor;
    [SerializeField] private Vector3 mainRotorAxis = Vector3.up;
    [SerializeField] private float mainRotorSpeed = 1080f;

    [SerializeField] private Transform tailRotor;
    [SerializeField] private Vector3 tailRotorAxis = Vector3.right;
    [SerializeField] private float tailRotorSpeed = 1440f;

    // =========================================================
    // DEATH AND DAMAGE
    // =========================================================

    [Header("TEMP")]
    private HealthComponent _healthComponent;
    [SerializeField] private float _controlLossthreshold = 0.2f;
    [SerializeField] private float _spinningForce = 15;
    [SerializeField] private float _fallingForce = 9.81f;
    private bool _lostControl = false;

    [Header("Critical Damage Spin")]
    [SerializeField] private Transform helicopterModel;
    [SerializeField] private float spinSpeed = 180f;
    [SerializeField] private float descentSpeed = 5f;
    [SerializeField] private float spinAcceleration = 90f;

    private bool isCriticallyDamaged;
    private float currentSpinSpeed;

    // =========================================================
    // INTERNAL STATE
    // =========================================================

    private Rigidbody rb;

    private float bodyYaw;
    private float yawVelocity;

    private Vector2 smoothedInput;
    private Vector2 inputVelocity;

    private float smoothedVertical;
    private float verticalVelocity;

    private float thrustX;
    private float thrustXVelocity;

    private float thrustZ;
    private float thrustZVelocity;

    private float accelX;
    private float accelZ;

    private Vector3 localVelocity;
    private Vector3 modelBasePosition;

    private Vector3 previousTargetPosition;
    private Vector3 targetVelocity;

    private float currentTargetDistance;

    // =========================================================
    // PUBLIC ACCESS
    // =========================================================

    public Behaviour CurrentBehaviour => currentBehaviour;
    public Maneuver CurrentManeuver => currentManeuver;
    public Vector3 Velocity => GetVelocity();
    public Transform Target => target;

    /// <summary>Angle between the helicopter's forward and the target. Handy for your missile script.</summary>
    public float AngleToTarget
    {
        get
        {
            if (target == null) return 180f;
            return Vector3.Angle(transform.forward, target.position - transform.position);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void SetBehaviour(Behaviour newBehaviour)
    {
        EnterBehaviour(newBehaviour);
    }

    private void EnterBehaviour(Behaviour newBehaviour)
    {
        currentBehaviour = newBehaviour;

        if (newBehaviour == Behaviour.Roam)
            PickRoamDestination();

        if (newBehaviour == Behaviour.Dogfight)
            StartManeuver(Maneuver.Approach);
    }

    private float Health01()
    {
        if (_healthComponent == null || _healthComponent.MaxHealth <= 0f)
            return 1f;

        return Mathf.Clamp01(_healthComponent.CurrentHealth / _healthComponent.MaxHealth);
    }

    // =========================================================
    // INITIALIZATION
    // =========================================================

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _healthComponent = GetComponent<HealthComponent>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = 0f;
#else
        rb.drag = 0f;
#endif

        bodyYaw = transform.eulerAngles.y;

        if (modelRoot != null)
            modelBasePosition = modelRoot.localPosition;
    }

    private void Start()
    {
        roamCenter = transform.position;
        EnterBehaviour(defaultBehaviour);
        PickRoamDestination();

        if (target != null)
            previousTargetPosition = target.position;
    }

    private void OnEnable()
    {
        if (_healthComponent != null)
            _healthComponent.OnDamageEventUI += _healthComponent_OnDamageEvent;
    }

    private void OnDisable()
    {
        if (_healthComponent != null)
            _healthComponent.OnDamageEventUI -= _healthComponent_OnDamageEvent;
    }

    private void _healthComponent_OnDamageEvent(float arg1)
    {
        if (Health01() <= _controlLossthreshold)
        {
            StartCriticalDamage();
            return;
        }

        // Reactive jink when hit.
        if (currentBehaviour == Behaviour.Dogfight &&
            Time.time >= nextReactiveEvadeTime &&
            Random.value < evadeOnDamageChance)
        {
            StartManeuver(Maneuver.Evade);
            nextReactiveEvadeTime = Time.time + reactiveEvadeCooldown;
        }
    }

    public void StartCriticalDamage()
    {
        if (isCriticallyDamaged)
            return;

        isCriticallyDamaged = true;
        currentSpinSpeed = 0f;
        _lostControl = true;
    }

    private void CriticalDamageUpdate()
    {
        if (!isCriticallyDamaged)
            return;

        currentSpinSpeed = Mathf.MoveTowards(
            currentSpinSpeed,
            spinSpeed,
            spinAcceleration * Time.deltaTime
        );

        if (helicopterModel != null)
        {
            helicopterModel.Rotate(
                Vector3.up,
                currentSpinSpeed * Time.deltaTime,
                Space.Self
            );
        }

        Vector3 velocity = GetVelocity();
        velocity.y = -descentSpeed;
        SetVelocity(velocity);
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (isCriticallyDamaged)
        {
            CriticalDamageUpdate();

            if (mainRotor != null)
                mainRotor.Rotate(mainRotorAxis * mainRotorSpeed * Time.deltaTime, Space.Self);

            if (tailRotor != null)
                tailRotor.Rotate(tailRotorAxis * tailRotorSpeed * Time.deltaTime, Space.Self);

            return;
        }

        UpdateTargetTracking();
        UpdateBehaviour();
        UpdateVisuals(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (_lostControl)
            return;

        float dt = Time.fixedDeltaTime;

        Vector3 desiredDirection = GetDesiredDirection();

        if (avoidObstacles)
            desiredDirection = AvoidObstacles(desiredDirection);

        // Convert desired world direction into local yaw space.
        Vector3 localDirection =
            Quaternion.Inverse(Quaternion.Euler(0f, bodyYaw, 0f))
            * desiredDirection;

        localDirection = Vector3.ClampMagnitude(localDirection, 1f);

        Vector2 rawInput = new Vector2(localDirection.x, localDirection.z);
        float rawVertical = localDirection.y;

        smoothedInput = Vector2.SmoothDamp(
            smoothedInput, rawInput, ref inputVelocity,
            inputSmoothTime, Mathf.Infinity, dt);

        smoothedVertical = Mathf.SmoothDamp(
            smoothedVertical, rawVertical, ref verticalVelocity,
            inputSmoothTime, Mathf.Infinity, dt);

        // Decide where to face. In a dogfight this is the target,
        // not the movement direction, which is what enables strafing.
        bool dogfighting = currentBehaviour == Behaviour.Dogfight && target != null;

        Vector3 facingDirection = dogfighting
            ? GetAimDirection()
            : desiredDirection;

        facingDirection.y = 0f;

        if (facingDirection.sqrMagnitude > 0.01f)
        {
            float desiredYaw = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;

            bodyYaw = Mathf.SmoothDampAngle(
                bodyYaw,
                desiredYaw,
                ref yawVelocity,
                dogfighting ? dogfightYawSmoothTime : yawSmoothTime,
                dogfighting ? dogfightYawSpeed : maxYawSpeed,
                dt
            );
        }

        rb.MoveRotation(Quaternion.Euler(0f, bodyYaw, 0f));

        Quaternion yawRotation = Quaternion.Euler(0f, bodyYaw, 0f);
        Vector3 local = Quaternion.Inverse(yawRotation) * GetVelocity();

        local.x = StepWithThrust(
            local.x, smoothedInput.x, rightAxis, leftAxis,
            ref thrustX, ref thrustXVelocity, out accelX, dt);

        local.z = StepWithThrust(
            local.z, smoothedInput.y, forwardAxis, backwardAxis,
            ref thrustZ, ref thrustZVelocity, out accelZ, dt);

        ComputeTargetAndRate(
            local.y, smoothedVertical, upAxis, downAxis,
            out float targetY, out float rateY);

        local.y = Mathf.MoveTowards(local.y, targetY, rateY * dt);

        localVelocity = local;

        SetVelocity(yawRotation * local);
    }

    // =========================================================
    // BEHAVIOUR SELECTION
    // =========================================================

    private void UpdateTargetTracking()
    {
        if (target == null)
            return;

        float dt = Mathf.Max(Time.deltaTime, 0.001f);

        targetVelocity = (target.position - previousTargetPosition) / dt;
        previousTargetPosition = target.position;

        currentTargetDistance = Vector3.Distance(transform.position, target.position);

        if (!detectTargetAutomatically)
            return;

        if (currentBehaviour == Behaviour.Chase ||
            currentBehaviour == Behaviour.Orbit ||
            currentBehaviour == Behaviour.Dogfight)
        {
            if (currentTargetDistance > loseTargetRange)
                EnterBehaviour(defaultBehaviour);
        }
        else if (currentTargetDistance <= detectionRange && HasLineOfSight())
        {
            EnterBehaviour(useDogfight ? Behaviour.Dogfight : Behaviour.Chase);
        }
    }

    private void UpdateBehaviour()
    {
        switch (currentBehaviour)
        {
            case Behaviour.Patrol:
                UpdatePatrol();
                break;

            case Behaviour.Roam:
                UpdateRoam();
                break;

            case Behaviour.Dogfight:
                UpdateDogfight();
                break;
        }
    }

    // =========================================================
    // DOGFIGHT
    // =========================================================

    private void UpdateDogfight()
    {
        if (target == null)
            return;

        float dt = Time.deltaTime;
        float dist = currentTargetDistance;

        maneuverTimer -= dt;

        // Strafe / orbit flip direction every so often so it's not predictable.
        if (currentManeuver == Maneuver.Strafe || currentManeuver == Maneuver.Orbit)
        {
            sideFlipTimer -= dt;

            if (sideFlipTimer <= 0f)
            {
                // Strafe flips often, orbit only occasionally.
                if (currentManeuver == Maneuver.Strafe || Random.value < 0.3f)
                    maneuverSide = -maneuverSide;

                sideFlipTimer = Random.Range(strafeFlipInterval.x, strafeFlipInterval.y);
            }
        }

        // Interrupts.
        if (currentManeuver == Maneuver.Approach &&
            dist <= (minCombatDistance + maxCombatDistance) * 0.5f)
        {
            maneuverTimer = 0f; // we're close enough, pick something else
        }

        if (currentManeuver != Maneuver.Approach &&
            currentManeuver != Maneuver.Evade &&
            dist > maxCombatDistance * 1.5f)
        {
            StartManeuver(Maneuver.Approach);
            return;
        }

        if ((currentManeuver == Maneuver.Approach || currentManeuver == Maneuver.Strafe || currentManeuver == Maneuver.Orbit) &&
            dist < minCombatDistance * 0.6f)
        {
            StartManeuver(Maneuver.Retreat);
            return;
        }

        if (maneuverTimer <= 0f)
            StartManeuver(PickManeuver(dist));
    }

    private Maneuver PickManeuver(float dist)
    {
        float health = Health01();

        float wApproach = Mathf.Max(0f, approachManeuver.weightByHealth.Evaluate(health));
        float wStrafe = Mathf.Max(0f, strafeManeuver.weightByHealth.Evaluate(health));
        float wOrbit = Mathf.Max(0f, orbitManeuver.weightByHealth.Evaluate(health));
        float wEvade = Mathf.Max(0f, evadeManeuver.weightByHealth.Evaluate(health));
        float wRetreat = Mathf.Max(0f, retreatManeuver.weightByHealth.Evaluate(health));

        if (dist > maxCombatDistance)
        {
            wApproach *= farApproachBonus;
            wStrafe *= 0.3f;
            wOrbit *= 0.3f;
        }
        else if (dist < minCombatDistance)
        {
            wApproach = 0f;
            wRetreat *= closeRetreatBonus;
            wEvade *= 1.5f;
        }
        else
        {
            // Already in the sweet spot, no need to approach much.
            wApproach *= 0.4f;
            wRetreat *= 0.3f;
        }

        // Don't repeat the same maneuver back-to-back.
        switch (currentManeuver)
        {
            case Maneuver.Approach: wApproach = 0f; break;
            case Maneuver.Strafe: wStrafe = 0f; break;
            case Maneuver.Orbit: wOrbit = 0f; break;
            case Maneuver.Evade: wEvade = 0f; break;
            case Maneuver.Retreat: wRetreat = 0f; break;
        }

        float total = wApproach + wStrafe + wOrbit + wEvade + wRetreat;

        if (total <= 0.0001f)
            return Maneuver.Strafe;

        float roll = Random.value * total;

        if ((roll -= wApproach) <= 0f) return Maneuver.Approach;
        if ((roll -= wStrafe) <= 0f) return Maneuver.Strafe;
        if ((roll -= wOrbit) <= 0f) return Maneuver.Orbit;
        if ((roll -= wEvade) <= 0f) return Maneuver.Evade;
        return Maneuver.Retreat;
    }

    private void StartManeuver(Maneuver maneuver)
    {
        currentManeuver = maneuver;

        ManeuverSettings settings = GetManeuverSettings(maneuver);
        maneuverTimer = Random.Range(settings.duration.x, settings.duration.y);

        maneuverSide = Random.value < 0.5f ? -1f : 1f;
        evadeVerticalSign = Random.value < 0.5f ? -1f : 1f;
        maneuverHeight = Random.Range(combatHeightRange.x, combatHeightRange.y);
        sideFlipTimer = Random.Range(strafeFlipInterval.x, strafeFlipInterval.y);
    }

    private ManeuverSettings GetManeuverSettings(Maneuver maneuver)
    {
        switch (maneuver)
        {
            case Maneuver.Approach: return approachManeuver;
            case Maneuver.Strafe: return strafeManeuver;
            case Maneuver.Orbit: return orbitManeuver;
            case Maneuver.Evade: return evadeManeuver;
            default: return retreatManeuver;
        }
    }

    private float RadialCorrection(float dist)
    {
        float band = Mathf.Max(distanceCorrectionBand, 0.01f);

        if (dist < minCombatDistance)
            return -Mathf.Clamp01((minCombatDistance - dist) / band);

        if (dist > maxCombatDistance)
            return Mathf.Clamp01((dist - maxCombatDistance) / band);

        return 0f;
    }

    private Vector3 GetAimDirection()
    {
        Vector3 aimPoint = target.position + targetVelocity * aimPrediction;
        return aimPoint - transform.position;
    }

    private Vector3 GetDogfightDirection()
    {
        if (target == null)
            return Vector3.zero;

        Vector3 toTarget = target.position - transform.position;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        float dist = flat.magnitude;

        if (dist < 0.01f)
            return Vector3.zero;

        Vector3 toward = flat / dist;
        Vector3 right = Vector3.Cross(Vector3.up, toward);

        float heightError = target.position.y + maneuverHeight - transform.position.y;
        Vector3 vertical = Vector3.up * Mathf.Clamp(heightError * heightCorrectionGain, -1f, 1f);
        Vector3 radial = toward * RadialCorrection(dist);

        Vector3 result;

        switch (currentManeuver)
        {
            case Maneuver.Approach:
                {
                    float closing = Mathf.Clamp01((dist - minCombatDistance) / Mathf.Max(distanceCorrectionBand, 0.01f));
                    float weave = Mathf.Sin(Time.time * approachWeaveFrequency * Mathf.PI * 2f) * approachWeave;
                    result = toward * closing + right * weave + vertical;
                    break;
                }

            case Maneuver.Strafe:
                result = right * (maneuverSide * strafeIntensity) + radial * 0.7f + vertical * 0.5f;
                break;

            case Maneuver.Orbit:
                result = right * (maneuverSide * orbitIntensity) + radial + vertical * 0.5f;
                break;

            case Maneuver.Evade:
                result = right * maneuverSide
                       + Vector3.up * (evadeVerticalSign * evadeVertical)
                       - toward * evadeBackward;
                break;

            default: // Retreat
                result = -toward + Vector3.up * 0.3f + right * (maneuverSide * 0.3f);
                break;
        }

        return Vector3.ClampMagnitude(result, 1f);
    }

    // =========================================================
    // PATROL
    // =========================================================

    private void UpdatePatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return;

        Transform waypoint = patrolPoints[currentWaypoint];

        if (waypoint == null)
            return;

        if (Vector3.Distance(transform.position, waypoint.position) <= waypointReachDistance)
        {
            currentWaypoint++;

            if (currentWaypoint >= patrolPoints.Length)
                currentWaypoint = loopPatrol ? 0 : patrolPoints.Length - 1;
        }
    }

    // =========================================================
    // ROAM
    // =========================================================

    private void UpdateRoam()
    {
        if (roamWaitTimer > 0f)
        {
            roamWaitTimer -= Time.deltaTime;
            return;
        }

        if (Vector3.Distance(transform.position, roamDestination) <= roamReachDistance)
        {
            PickRoamDestination();
            roamWaitTimer = roamWaitTime;
        }
    }

    private void PickRoamDestination()
    {
        Vector2 randomCircle = Random.insideUnitCircle * roamRadius;

        roamDestination = roamCenter + new Vector3(
            randomCircle.x,
            Random.Range(-roamAltitude * 0.5f, roamAltitude * 0.5f),
            randomCircle.y
        );
    }

    // =========================================================
    // DESIRED FLIGHT DIRECTION
    // =========================================================

    private Vector3 GetDesiredDirection()
    {
        switch (currentBehaviour)
        {
            case Behaviour.Patrol:
                return GetPatrolDirection();

            case Behaviour.Roam:
                return GetDestinationDirection(roamDestination);

            case Behaviour.Chase:
                return GetChaseDirection();

            case Behaviour.Orbit:
                return GetOrbitDirection();

            case Behaviour.Dogfight:
                return GetDogfightDirection();
        }

        return transform.forward;
    }

    private Vector3 GetPatrolDirection()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return Vector3.zero;

        Transform waypoint = patrolPoints[currentWaypoint];

        if (waypoint == null)
            return Vector3.zero;

        Vector3 destination = waypoint.position;
        destination.y += patrolAltitude;

        return GetDestinationDirection(destination);
    }

    private Vector3 GetDestinationDirection(Vector3 destination)
    {
        return (destination - transform.position).normalized;
    }

    private Vector3 GetChaseDirection()
    {
        if (target == null)
            return transform.forward;

        Vector3 predictedPosition = target.position + targetVelocity * chasePrediction;
        Vector3 destination = predictedPosition + Vector3.up * chaseHeight;
        Vector3 offset = destination - transform.position;

        float horizontalDistance = new Vector3(offset.x, 0f, offset.z).magnitude;

        if (horizontalDistance < chaseDistance)
        {
            Vector3 away = new Vector3(offset.x, 0f, offset.z).normalized;
            offset -= away * (chaseDistance - horizontalDistance);
        }

        if (enableStrafing)
        {
            Vector3 right = Vector3.Cross(Vector3.up, offset.normalized);

            float strafe = Mathf.Sin(
                Time.time * strafeFrequency * Mathf.PI * 2f
            ) * strafeStrength;

            offset += right * strafe * 10f;
        }

        return offset.normalized;
    }

    private Vector3 GetOrbitDirection()
    {
        if (target == null)
            return transform.forward;

        Vector3 toTarget = target.position - transform.position;
        Vector3 horizontal = new Vector3(toTarget.x, 0f, toTarget.z);
        float distance = horizontal.magnitude;

        if (distance < 0.01f)
            return transform.forward;

        Vector3 towardTarget = horizontal.normalized;

        Vector3 tangent = Vector3.Cross(Vector3.up, towardTarget) * Mathf.Sign(orbitDirection);

        Vector3 orbitMovement = tangent * orbitSpeed;

        float distanceError = distance - orbitDistance;
        Vector3 radialCorrection = towardTarget * distanceError * orbitDistanceCorrection;

        float heightError = target.position.y + orbitHeight - transform.position.y;
        Vector3 verticalCorrection = Vector3.up * heightError;

        Vector3 direction = orbitMovement + radialCorrection + verticalCorrection;

        if (enableStrafing)
        {
            direction += tangent *
                Mathf.Sin(Time.time * strafeFrequency * Mathf.PI * 2f)
                * strafeStrength * 5f;
        }

        return direction.normalized;
    }

    // =========================================================
    // OBSTACLE AVOIDANCE
    // =========================================================

    private Vector3 AvoidObstacles(Vector3 desiredDirection)
    {
        if (desiredDirection.sqrMagnitude < 0.001f)
            return desiredDirection;

        Vector3 origin = transform.position;
        Vector3 forward = desiredDirection.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.up;

        Vector3 avoidance = Vector3.zero;

        CheckObstacle(origin, forward, ref avoidance, 1f);
        CheckObstacle(origin, (forward + right * 0.6f).normalized, ref avoidance, 1f);
        CheckObstacle(origin, (forward - right * 0.6f).normalized, ref avoidance, 1f);
        CheckObstacle(origin, (forward + up * 0.5f).normalized, ref avoidance, 0.8f);
        CheckObstacle(origin, (forward - up * 0.5f).normalized, ref avoidance, 0.8f);

        CheckObstacle(origin, right, ref avoidance, sideAvoidanceWeight);
        CheckObstacle(origin, -right, ref avoidance, sideAvoidanceWeight);

        if (avoidance.sqrMagnitude > 0.001f)
        {
            return (desiredDirection + avoidance.normalized * obstacleAvoidanceStrength).normalized;
        }

        return desiredDirection;
    }

    private void CheckObstacle(Vector3 origin, Vector3 direction, ref Vector3 avoidance, float weight)
    {
        if (Physics.SphereCast(
            origin,
            obstacleSphereRadius,
            direction,
            out RaycastHit hit,
            obstacleDetectionDistance,
            obstacleMask,
            QueryTriggerInteraction.Ignore))
        {
            float strength = 1f - (hit.distance / obstacleDetectionDistance);

            avoidance += hit.normal * strength * weight;

            if (Mathf.Abs(hit.normal.y) < 0.5f)
                avoidance += Vector3.up * strength * 0.4f;
        }
    }

    private bool HasLineOfSight()
    {
        if (target == null)
            return false;

        Vector3 direction = target.position - transform.position;

        if (Physics.Raycast(
            transform.position,
            direction.normalized,
            direction.magnitude,
            targetObstructionMask,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return true;
    }

    // =========================================================
    // MOVEMENT PHYSICS
    // =========================================================

    private void ComputeTargetAndRate(
        float current,
        float input,
        AxisSettings positive,
        AxisSettings negative,
        out float targetSpeed,
        out float rate)
    {
        if (Mathf.Abs(input) < 0.001f)
        {
            targetSpeed = 0f;
            rate = current >= 0f ? positive.deceleration : negative.deceleration;
            return;
        }

        bool wantsPositive = input > 0f;
        AxisSettings wanted = wantsPositive ? positive : negative;

        targetSpeed = input * wanted.maxSpeed;

        bool opposing =
            Mathf.Abs(current) > 0.05f &&
            (current > 0f) != wantsPositive;

        if (opposing)
        {
            AxisSettings moving = current > 0f ? positive : negative;
            rate = (wanted.acceleration + moving.deceleration) * counterThrustMultiplier;
        }
        else if (Mathf.Abs(targetSpeed) < Mathf.Abs(current))
        {
            rate = wanted.deceleration;
        }
        else
        {
            rate = wanted.acceleration;
        }
    }

    private float StepWithThrust(
        float current,
        float input,
        AxisSettings positive,
        AxisSettings negative,
        ref float thrust,
        ref float thrustVelocity,
        out float accelCommand,
        float dt)
    {
        ComputeTargetAndRate(current, input, positive, negative, out float targetSpeed, out float rate);

        float difference = targetSpeed - current;

        accelCommand = Mathf.Clamp(
            difference / Mathf.Max(thrustEaseSpeed, 0.01f),
            -1f,
            1f
        );

        thrust = Mathf.SmoothDamp(
            thrust, accelCommand, ref thrustVelocity,
            thrustDelay, Mathf.Infinity, dt);

        float next = current + thrust * rate * dt;

        if (difference > 0f)
            next = Mathf.Min(next, targetSpeed);
        else if (difference < 0f)
            next = Mathf.Max(next, targetSpeed);

        if (Mathf.Abs(targetSpeed - next) < 0.02f)
            next = targetSpeed;

        float minSpeed = Mathf.Min(-negative.maxSpeed, current);
        float maxSpeed = Mathf.Max(positive.maxSpeed, current);

        return Mathf.Clamp(next, minSpeed, maxSpeed);
    }

    // =========================================================
    // VISUALS
    // =========================================================

    private void UpdateVisuals(float dt)
    {
        if (mainRotor != null)
            mainRotor.Rotate(mainRotorAxis * mainRotorSpeed * dt, Space.Self);

        if (tailRotor != null)
            tailRotor.Rotate(tailRotorAxis * tailRotorSpeed * dt, Space.Self);

        if (modelRoot == null)
            return;

        float forwardSpeed = NormalizeSpeed(localVelocity.z, forwardAxis, backwardAxis);
        float sideSpeed = NormalizeSpeed(localVelocity.x, rightAxis, leftAxis);

        float pitch = accelZ * maxPitchTilt + forwardSpeed * cruiseTilt;
        float roll = -accelX * maxBankTilt - sideSpeed * cruiseTilt;

        Quaternion targetRotation = Quaternion.Euler(pitch, 0f, roll);

        modelRoot.localRotation = Quaternion.Slerp(
            modelRoot.localRotation,
            targetRotation,
            1f - Mathf.Exp(-tiltResponse * dt)
        );

        float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;

        modelRoot.localPosition = modelBasePosition + Vector3.up * bob;
    }

    private float NormalizeSpeed(float speed, AxisSettings positive, AxisSettings negative)
    {
        float normalized = speed >= 0f
            ? speed / Mathf.Max(positive.maxSpeed, 0.01f)
            : speed / Mathf.Max(negative.maxSpeed, 0.01f);

        return Mathf.Clamp(normalized, -1f, 1f);
    }

    // =========================================================
    // RIGIDBODY HELPERS
    // =========================================================

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    private void SetVelocity(Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        if (currentBehaviour == Behaviour.Roam)
            Gizmos.DrawWireSphere(roamCenter, roamRadius);

        if (currentBehaviour == Behaviour.Orbit && target != null)
            Gizmos.DrawWireSphere(target.position, orbitDistance);

        if (target != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(target.position, detectionRange);

            // Dogfight combat band.
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(target.position, minCombatDistance);
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(target.position, maxCombatDistance);
        }

        if (patrolPoints != null)
        {
            Gizmos.color = Color.cyan;

            foreach (Transform point in patrolPoints)
            {
                if (point != null)
                    Gizmos.DrawSphere(point.position, 1f);
            }
        }

        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.forward * obstacleDetectionDistance);
    }
}