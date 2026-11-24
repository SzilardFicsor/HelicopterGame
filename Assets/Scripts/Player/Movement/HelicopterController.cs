using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arcade helicopter controller (Rigidbody + new Input System).
///
/// MOUSE  = moves the camera / aim direction. The helicopter turns to follow the aim.
/// WASD   = move forward / left / back / right relative to the helicopter's heading.
/// Ascend / Descend = vertical movement.
///
/// Visual tilt is driven by what the player is COMMANDING (acceleration), not by speed,
/// and the real thrust ramps in slightly after the tilt, so the heli visibly leans first
/// and then accelerates / brakes.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HelicopterController : MonoBehaviour
{
    [System.Serializable]
    public struct AxisSettings
    {
        [Tooltip("Top speed on this axis (m/s).")]
        public float maxSpeed;
        [Tooltip("How fast speed builds up while the input is held (m/s²).")]
        public float acceleration;
        [Tooltip("How fast speed bleeds off when the input is released (m/s²). Lower = floatier.")]
        public float deceleration;

        public AxisSettings(float maxSpeed, float acceleration, float deceleration)
        {
            this.maxSpeed = maxSpeed;
            this.acceleration = acceleration;
            this.deceleration = deceleration;
        }
    }

    // ------------------------------------------------------------------ INPUT
    [Header("Input (drag actions from your Input Actions asset)")]
    [Tooltip("Vector2 - WASD.")]
    [SerializeField] private InputActionReference moveAction;
    [Tooltip("Vector2 - Mouse delta.")]
    [SerializeField] private InputActionReference lookAction;
    [Tooltip("Button/float - Space.")]
    [SerializeField] private InputActionReference ascendAction;
    [Tooltip("Button/float - Left Ctrl (or Shift).")]
    [SerializeField] private InputActionReference descendAction;
    [Tooltip("Locks the cursor. Esc releases it, clicking locks it again.")]
    [SerializeField] private bool lockCursor = true;

    // --------------------------------------------------------------- MOVEMENT
    [Header("Movement - per direction")]
    [SerializeField] private AxisSettings forwardAxis = new AxisSettings(25f, 10f, 4f);
    [SerializeField] private AxisSettings backwardAxis = new AxisSettings(12f, 8f, 5f);
    [SerializeField] private AxisSettings rightAxis = new AxisSettings(14f, 8f, 4f);
    [SerializeField] private AxisSettings leftAxis = new AxisSettings(14f, 8f, 4f);
    [SerializeField] private AxisSettings upAxis = new AxisSettings(8f, 6f, 5f);
    [SerializeField] private AxisSettings downAxis = new AxisSettings(10f, 8f, 6f);

    // ---------------------------------------------------------- DAMAGE SYSTEM

    [Header("Damage System")]
    [SerializeField] private HealthComponent healthComponent;

    [Range(0f, 1f)]
    [SerializeField] private float sluggishHealthThreshold = 0.75f;

    [Range(0f, 1f)]
    [SerializeField] private float stallHealthThreshold = 0.50f;

    [Range(0f, 1f)]
    [SerializeField] private float criticalHealthThreshold = 0.20f;

    [Header("Sluggish Controls")]
    [Range(0.1f, 1f)]
    [SerializeField] private float damagedSpeedMultiplier = 0.7f;

    [Range(0.1f, 1f)]
    [SerializeField] private float damagedAccelerationMultiplier = 0.5f;

    [Range(0.1f, 1f)]
    [SerializeField] private float damagedTurningMultiplier = 0.55f;

    [Header("Random Engine Stalling")]
    [SerializeField] private Vector2 stallInterval = new Vector2(8f, 18f);
    [SerializeField] private Vector2 stallDuration = new Vector2(1.5f, 3.5f);
    [SerializeField] private float stallDropSpeed = 7f;

    [Header("Critical Damage - Spiral")]
    [SerializeField] private float criticalDescentSpeed = 8f;
    [SerializeField] private float criticalSpiralSpeed = 5f;
    [SerializeField] private float criticalSpiralRate = 100f;
    [SerializeField] private float criticalSpinSpeed = 540f;
    [SerializeField] private float criticalSpinAcceleration = 180f;

    private bool isSluggish;
    private bool isStalling;
    private bool isCritical;

    private float stallCooldown;
    private float stallRemaining;

    private float criticalSpinAngle;
    private float currentCriticalSpinSpeed;

    private Quaternion modelBaseRotation;

    [Header("Movement - feel")]
    [Tooltip("Smooths the raw key input. Higher = floatier, less twitchy.")]
    [SerializeField] private float inputSmoothTime = 0.06f;
    [Tooltip("Multiplier on braking when you press the opposite direction. 1 = normal.")]
    [SerializeField] private float counterThrustMultiplier = 1f;
    [Tooltip("ANTICIPATION: how long thrust takes to ramp up/reverse after you change input. " +
             "The heli tilts immediately, then the speed change follows. 0 = no delay, 0.15-0.3 = clear lean-before-move.")]
    [SerializeField] private float thrustDelay = 0.15f;
    [Tooltip("Speed difference (m/s) over which thrust fades out as you near your target speed. " +
             "Also controls how long the 'accelerating' tilt lasts before settling into the cruise tilt.")]
    [SerializeField] private float thrustEaseSpeed = 2f;

    // -------------------------------------------------------------- ROTATION
    [Header("Aim (mouse moves the camera, heli follows)")]
    [Tooltip("Degrees per mouse count. Mouse delta is not frame-rate scaled.")]
    [SerializeField] private Vector2 lookSensitivity = new Vector2(0.08f, 0.08f);
    [SerializeField] private bool invertY = false;
    [Tooltip("How far the camera/aim can look up.")]
    [SerializeField] private float lookPitchUp = 35f;
    [Tooltip("How far the camera/aim can look down.")]
    [SerializeField] private float lookPitchDown = 50f;

    [Header("Helicopter body following the aim")]
    [Tooltip("Time for the heli to catch up with the aim direction. Higher = heavier, floatier turning.")]
    [SerializeField] private float yawSmoothTime = 0.15f;
    [Tooltip("Hard cap on turn speed (deg/s).")]
    [SerializeField] private float maxYawSpeed = 220f;
    [Tooltip("Max nose-up angle of the heli body when you aim up.")]
    [SerializeField] private float bodyPitchUp = 15f;
    [Tooltip("Max nose-down angle of the heli body when you aim down.")]
    [SerializeField] private float bodyPitchDown = 12f;
    [SerializeField] private float pitchSmoothTime = 0.12f;

    // ---------------------------------------------------------------- VISUALS
    [Header("Visual tilt - driven by input / commanded acceleration")]
    [Tooltip("Child transform holding the mesh. Tilt and bob are applied here so physics stay clean.")]
    [SerializeField] private Transform modelRoot;
    [Tooltip("Nose-DOWN angle while speeding up forward.")]
    [SerializeField] private float accelPitchTilt = 16f;
    [Tooltip("Nose-UP angle while slowing down (or accelerating backward). Make this bigger than the cruise tilt so braking visibly pitches the nose up.")]
    [SerializeField] private float brakePitchTilt = 20f;
    [Tooltip("Roll angle while accelerating sideways. Also used to counter-tilt when braking a sideways drift.")]
    [SerializeField] private float accelBank = 16f;
    [Tooltip("Small steady nose-down tilt while cruising forward at full speed.")]
    [SerializeField] private float cruisePitchTilt = 6f;
    [Tooltip("Small steady roll while drifting sideways at full speed.")]
    [SerializeField] private float cruiseBank = 4f;
    [Tooltip("Extra roll into turns, based on turn speed.")]
    [SerializeField] private float turnBank = 12f;
    [Tooltip("How quickly the visual tilt follows its target. Higher = snappier lean (leads the thrust more clearly).")]
    [SerializeField] private float tiltResponse = 7f;

    [Header("Hover feel")]
    [Tooltip("Hover bob height in metres.")]
    [SerializeField] private float bobAmplitude = 0.12f;
    [Tooltip("Hover bob cycles per second.")]
    [SerializeField] private float bobFrequency = 0.6f;
    [Tooltip("Small constant pitch/roll wobble in degrees.")]
    [SerializeField] private float swayAngle = 0.8f;

    [Header("Rotors (optional)")]
    [SerializeField] private Transform mainRotor;
    [SerializeField] private Vector3 mainRotorAxis = Vector3.up;
    [SerializeField] private float mainRotorSpeed = 1080f;
    [SerializeField] private Transform tailRotor;
    [SerializeField] private Vector3 tailRotorAxis = Vector3.right;
    [SerializeField] private float tailRotorSpeed = 1440f;

    // ----------------------------------------------------------------- STATE

    private Vector2 LookInput;
    private Rigidbody rb;

    // Aim (what the mouse controls). Pitch: positive = looking UP.
    private float aimYaw;
    private float aimPitch;

    // Heli body (follows the aim)
    private float bodyYaw, bodyYawVelocity;
    private float bodyPitch, bodyPitchVelocity;

    private Vector2 smoothedMove, moveSmoothVelocity;
    private float smoothedVertical, verticalSmoothVelocity;

    // Thrust ramp (per axis) - this is what delays the real acceleration behind the tilt
    private float thrustX, thrustXVelocity;
    private float thrustZ, thrustZVelocity;

    // Commanded acceleration, -1..1. Drives the visual tilt immediately.
    private float accelCmdX, accelCmdZ;

    private Vector3 localVelocity; // x = right, y = up, z = forward (relative to body yaw)
    private Vector3 modelBasePosition;

    // ----------------------------------------------------- PUBLIC READ-OUTS
    /// <summary>Where the player is looking/aiming (yaw, degrees). Camera follows this.</summary>
    public float AimYaw => aimYaw;
    /// <summary>Where the player is looking/aiming (pitch, degrees, positive = up).</summary>
    public float AimPitch => aimPitch;
    /// <summary>World-space direction the player is aiming.</summary>
    public Vector3 AimDirection => Quaternion.Euler(-aimPitch, aimYaw, 0f) * Vector3.forward;
    /// <summary>The helicopter body's actual heading (lags behind the aim).</summary>
    public float Yaw => bodyYaw;
    public Vector3 LocalVelocity => localVelocity;

    public Vector3 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        private set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    // ------------------------------------------------------------- LIFECYCLE
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation; // rotation is driven by this script
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = 0f;
#else
        rb.drag = 0f;
#endif

        aimYaw = bodyYaw = transform.eulerAngles.y;
        if (modelRoot != null)
        {
            modelBasePosition = modelRoot.localPosition;
            modelBaseRotation = modelRoot.localRotation;
        }
    }

    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnEnable()
    {
        Enable(moveAction); Enable(lookAction); Enable(ascendAction); Enable(descendAction);
        if (healthComponent == null)
            healthComponent = GetComponent<HealthComponent>();

        if (healthComponent != null)
        {
            healthComponent.OnDamageEvent += HandleHealthChanged;


        }

    }

    private void OnDisable()
    {
        Disable(moveAction); Disable(lookAction); Disable(ascendAction); Disable(descendAction);
        if (healthComponent != null)
        {
            healthComponent.OnDamageEvent -= HandleHealthChanged;


        }
    }

    private void OnValidate()
    {
        inputSmoothTime = Mathf.Max(0f, inputSmoothTime);
        thrustDelay = Mathf.Max(0f, thrustDelay);
        thrustEaseSpeed = Mathf.Max(0.01f, thrustEaseSpeed);
        yawSmoothTime = Mathf.Max(0.001f, yawSmoothTime);
        pitchSmoothTime = Mathf.Max(0.001f, pitchSmoothTime);
        maxYawSpeed = Mathf.Max(1f, maxYawSpeed);
        counterThrustMultiplier = Mathf.Max(0f, counterThrustMultiplier);
    }
    private void HandleHealthChanged(float arg1)
    {
        float healthPercent = healthComponent.CurrentHealth / healthComponent.MaxHealth;

        isSluggish = healthPercent <= sluggishHealthThreshold;

        if (healthPercent <= criticalHealthThreshold)
        {
            if (!isCritical)
                StartCriticalDamage();

            return;
        }

        isCritical = false;

        if (healthPercent <= stallHealthThreshold && stallCooldown <= 0f)
            stallCooldown = Random.Range(stallInterval.x, stallInterval.y);
    }
    private void StartCriticalDamage()
    {
        isCritical = true;

        isStalling = false;
        stallRemaining = 0f;

        currentCriticalSpinSpeed = 0f;
        criticalSpinAngle = 0f;

        // Clear normal flight inputs and thrust.
        smoothedMove = Vector2.zero;
        smoothedVertical = 0f;

        thrustX = 0f;
        thrustZ = 0f;

        accelCmdX = 0f;
        accelCmdZ = 0f;
    }

    private void UpdateStalling(float dt)
    {
        float healthPercent = healthComponent.CurrentHealth / healthComponent.MaxHealth;
        if (isSluggish == false ||
            healthComponent == null ||
            healthPercent > stallHealthThreshold)
        {
            isStalling = false;
            stallRemaining = 0f;
            return;
        }

        if (isStalling)
        {
            stallRemaining -= dt;

            if (stallRemaining <= 0f)
            {
                isStalling = false;
                stallCooldown = Random.Range(stallInterval.x, stallInterval.y);
            }

            return;
        }

        stallCooldown -= dt;

        if (stallCooldown <= 0f)
        {
            isStalling = true;
            stallRemaining = Random.Range(stallDuration.x, stallDuration.y);
        }
    }

    // ---------------------------------------------------------------- UPDATE
    // Mouse delta is a per-frame value, so we read and accumulate it in Update.
    private void Look_canceled(UnityEngine.InputSystem.InputAction.CallbackContext obj) { LookInput = Vector2.zero; }
    private void Look_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {

        LookInput = obj.ReadValue<Vector2>();

    }

    private void Update()
    {
        HandleCursor();

        Vector2 look = Vector2.zero;
        bool cursorOk = !lockCursor || Cursor.lockState == CursorLockMode.Locked;
        if (cursorOk && lookAction != null) look = lookAction.action.ReadValue<Vector2>();

        aimYaw += look.x * lookSensitivity.x;
        aimPitch += look.y * lookSensitivity.y * (invertY ? -1f : 1f);
        aimPitch = Mathf.Clamp(aimPitch, -lookPitchDown, lookPitchUp);

        UpdateVisuals(Time.deltaTime);
    }

    private AxisSettings ScaleAxis(
    AxisSettings axis,
    float speedMultiplier,
    float accelerationMultiplier)
    {
        return new AxisSettings(
            axis.maxSpeed * speedMultiplier,
            axis.acceleration * accelerationMultiplier,
            axis.deceleration * accelerationMultiplier
        );
    }
    private void CriticalFlightUpdate(float dt)
    {
        // Accelerate the visual spin.
        currentCriticalSpinSpeed = Mathf.MoveTowards(
            currentCriticalSpinSpeed,
            criticalSpinSpeed,
            criticalSpinAcceleration * dt
        );

        // Maintain horizontal movement while steering it into a spiral.
        Vector3 horizontalVelocity = new Vector3(
            Velocity.x,
            0f,
            Velocity.z
        );

        if (horizontalVelocity.magnitude < 0.5f)
            horizontalVelocity = transform.forward * criticalSpiralSpeed;

        horizontalVelocity = Quaternion.Euler(
            0f,
            criticalSpiralRate * dt,
            0f
        ) * horizontalVelocity;

        // Override normal flight control and force descent.
        Velocity = new Vector3(
            horizontalVelocity.x,
            -criticalDescentSpeed,
            horizontalVelocity.z
        );
    }
    // ----------------------------------------------------------- FIXEDUPDATE
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        // Critical damage overrides all normal flight movement.
        if (isCritical)
        {
            CriticalFlightUpdate(dt);
            return;
        }

        UpdateStalling(dt);

        // 1) Read + smooth input
        Vector2 rawMove = moveAction != null
            ? Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f)
            : Vector2.zero;
        float rawVertical = Mathf.Clamp(ReadFloat(ascendAction) - ReadFloat(descendAction), -1f, 1f);

        smoothedMove = Vector2.SmoothDamp(smoothedMove, rawMove, ref moveSmoothVelocity, inputSmoothTime, Mathf.Infinity, dt);
        smoothedVertical = Mathf.SmoothDamp(smoothedVertical, rawVertical, ref verticalSmoothVelocity, inputSmoothTime, Mathf.Infinity, dt);

        // 2) Body follows the aim: yaw fully, pitch only a limited amount
        float turnMultiplier = isSluggish
               ? damagedTurningMultiplier
               : 1f;

        float effectiveYawSmoothTime = yawSmoothTime / turnMultiplier;
        float effectiveMaxYawSpeed = maxYawSpeed * turnMultiplier;

        bodyYaw = Mathf.SmoothDampAngle(
            bodyYaw,
            aimYaw,
            ref bodyYawVelocity,
            effectiveYawSmoothTime,
            effectiveMaxYawSpeed,
            dt
        );
        //bodyYaw = Mathf.SmoothDampAngle(bodyYaw, aimYaw, ref bodyYawVelocity, yawSmoothTime, maxYawSpeed, dt);
        float desiredBodyPitch = Mathf.Clamp(aimPitch, -bodyPitchDown, bodyPitchUp);
        bodyPitch = Mathf.SmoothDamp(bodyPitch, desiredBodyPitch, ref bodyPitchVelocity, pitchSmoothTime, Mathf.Infinity, dt);
        rb.MoveRotation(Quaternion.Euler(-bodyPitch, bodyYaw, 0f));

        // 3) Velocity in yaw-local space (real rigidbody velocity is read every step, so collisions feed back)
        Quaternion yawRot = Quaternion.Euler(0f, bodyYaw, 0f);
        Vector3 local = Quaternion.Inverse(yawRot) * Velocity;
        float speedMultiplier = isSluggish ? damagedSpeedMultiplier : 1f;
        float accelerationMultiplier = isSluggish ? damagedAccelerationMultiplier : 1f;

        AxisSettings effectiveRight = ScaleAxis(rightAxis, speedMultiplier, accelerationMultiplier);
        AxisSettings effectiveLeft = ScaleAxis(leftAxis, speedMultiplier, accelerationMultiplier);
        AxisSettings effectiveForward = ScaleAxis(forwardAxis, speedMultiplier, accelerationMultiplier);
        AxisSettings effectiveBackward = ScaleAxis(backwardAxis, speedMultiplier, accelerationMultiplier);
        AxisSettings effectiveUp = ScaleAxis(upAxis, speedMultiplier, accelerationMultiplier);
        AxisSettings effectiveDown = ScaleAxis(downAxis, speedMultiplier, accelerationMultiplier);

        local.x = StepWithThrust(local.x, smoothedMove.x, effectiveRight, effectiveLeft,
            ref thrustX, ref thrustXVelocity, out accelCmdX, dt);

        local.z = StepWithThrust(local.z, smoothedMove.y, effectiveForward, effectiveBackward,
            ref thrustZ, ref thrustZVelocity, out accelCmdZ, dt);

        ComputeTargetAndRate(local.y, smoothedVertical, effectiveUp, effectiveDown,
            out float targetY, out float rateY);

        local.y = Mathf.MoveTowards(local.y, targetY, rateY * dt);

        // Random engine stall: force a temporary loss of altitude.
        if (isStalling)
        {
            local.y = Mathf.MoveTowards(
                local.y,
                -stallDropSpeed,
                stallDropSpeed * 4f * dt
            );
        }

        //local.x = StepWithThrust(local.x, smoothedMove.x, rightAxis, leftAxis, ref thrustX, ref thrustXVelocity, out accelCmdX, dt);
        //local.z = StepWithThrust(local.z, smoothedMove.y, forwardAxis, backwardAxis, ref thrustZ, ref thrustZVelocity, out accelCmdZ, dt);

        //ComputeTargetAndRate(local.y, smoothedVertical, upAxis, downAxis, out float targetY, out float rateY);
        //local.y = Mathf.MoveTowards(local.y, targetY, rateY * dt);

        localVelocity = local;
        Velocity = yawRot * local;
    }

    // ------------------------------------------------------------- MOVEMENT
    /// <summary>
    /// Works out the speed the input is asking for and how fast we may change speed.
    /// 'pos' is the +axis (right/forward/up), 'neg' is the -axis (left/back/down).
    /// </summary>
    private void ComputeTargetAndRate(float current, float input, AxisSettings pos, AxisSettings neg,
                                      out float target, out float rate)
    {
        if (Mathf.Abs(input) < 0.001f)
        {
            // No input: drift to a stop using the deceleration of the direction we're moving in.
            target = 0f;
            rate = current >= 0f ? pos.deceleration : neg.deceleration;
            return;
        }

        bool wantsPositive = input > 0f;
        AxisSettings wanted = wantsPositive ? pos : neg;
        target = input * wanted.maxSpeed;

        bool opposing = Mathf.Abs(current) > 0.05f && (current > 0f) != wantsPositive;
        if (opposing)
        {
            // Pushing against current motion: brake and thrust the other way.
            AxisSettings moving = current > 0f ? pos : neg;
            rate = (wanted.acceleration + moving.deceleration) * counterThrustMultiplier;
        }
        else if (Mathf.Abs(target) < Mathf.Abs(current))
        {
            rate = wanted.deceleration; // easing off the stick
        }
        else
        {
            rate = wanted.acceleration;
        }
    }

    /// <summary>
    /// One horizontal axis. 'accelCmd' (-1..1) is the acceleration the player is asking for RIGHT NOW,
    /// and drives the visual tilt. The real acceleration uses 'thrust', which follows accelCmd with a
    /// delay (thrustDelay). That delay is what makes the heli lean first and move second.
    /// </summary>
    private float StepWithThrust(float current, float input, AxisSettings pos, AxisSettings neg,
                                 ref float thrust, ref float thrustVelocity, out float accelCmd, float dt)
    {
        ComputeTargetAndRate(current, input, pos, neg, out float target, out float rate);

        float diff = target - current;
        accelCmd = Mathf.Clamp(diff / thrustEaseSpeed, -1f, 1f);

        thrust = Mathf.SmoothDamp(thrust, accelCmd, ref thrustVelocity, thrustDelay, Mathf.Infinity, dt);

        float next = current + thrust * rate * dt;

        // Don't overshoot the target when moving toward it
        if (diff > 0f) next = Mathf.Min(next, target);
        else if (diff < 0f) next = Mathf.Max(next, target);
        if (Mathf.Abs(target - next) < 0.02f) next = target;

        // Never exceed max speed (but don't fight outside forces that already pushed us past it)
        float lo = Mathf.Min(-neg.maxSpeed, current);
        float hi = Mathf.Max(pos.maxSpeed, current);
        return Mathf.Clamp(next, lo, hi);
    }

    // --------------------------------------------------------------- VISUALS
    private void UpdateVisuals(float dt)
    {
        if (mainRotor != null) mainRotor.Rotate(mainRotorAxis * (mainRotorSpeed * dt), Space.Self);
        if (tailRotor != null) tailRotor.Rotate(tailRotorAxis * (tailRotorSpeed * dt), Space.Self);

        if (modelRoot == null) return;
        // Critical damage: spin the helicopter model independently
        // of the Rigidbody, preserving physics rotation constraints.
        if (isCritical)
        {
            criticalSpinAngle += currentCriticalSpinSpeed * dt;

            modelRoot.localRotation =
                modelBaseRotation *
                Quaternion.AngleAxis(criticalSpinAngle, Vector3.up);

            // No normal hover bob or movement tilt during critical damage.
            return;
        }

        float nz = NormalizeSpeed(localVelocity.z, forwardAxis, backwardAxis);
        float nx = NormalizeSpeed(localVelocity.x, rightAxis, leftAxis);
        float turn = Mathf.Clamp(bodyYawVelocity / maxYawSpeed, -1f, 1f);

        float t = Time.time;

        // Unity: +X rotation = nose DOWN, +Z rotation = roll LEFT.
        // accelCmdZ > 0  -> speeding up forward        -> nose down
        // accelCmdZ < 0  -> slowing down / going back  -> nose up (tilts back BEFORE speed drops)
        float pitchTilt = (accelCmdZ >= 0f ? accelCmdZ * accelPitchTilt : accelCmdZ * brakePitchTilt)
                          + nz * cruisePitchTilt
                          + Mathf.Sin(t * bobFrequency * 1.3f) * swayAngle;

        // accelCmdX > 0 -> pushing right (incl. braking a leftward drift) -> bank right
        float roll = -(accelCmdX * accelBank + nx * cruiseBank)
                     - turn * turnBank
                     + Mathf.Cos(t * bobFrequency * 0.9f) * swayAngle;

        Quaternion targetRot = Quaternion.Euler(pitchTilt, 0f, roll);
        modelRoot.localRotation = Quaternion.Slerp(modelRoot.localRotation, targetRot, 1f - Mathf.Exp(-tiltResponse * dt));

        float bob = Mathf.Sin(t * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        modelRoot.localPosition = modelBasePosition + Vector3.up * bob;
    }

    private static float NormalizeSpeed(float v, AxisSettings pos, AxisSettings neg)
    {
        float n = v >= 0f ? v / Mathf.Max(pos.maxSpeed, 0.01f) : v / Mathf.Max(neg.maxSpeed, 0.01f);
        return Mathf.Clamp(n, -1f, 1f);
    }

    // ---------------------------------------------------------------- HELPERS
    private void HandleCursor()
    {
        if (!lockCursor) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Cursor.lockState != CursorLockMode.Locked &&
                 Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private static float ReadFloat(InputActionReference r) => r != null ? r.action.ReadValue<float>() : 0f;
    private static void Enable(InputActionReference r) { if (r != null) r.action.Enable(); }
    private static void Disable(InputActionReference r) { if (r != null) r.action.Disable(); }
}