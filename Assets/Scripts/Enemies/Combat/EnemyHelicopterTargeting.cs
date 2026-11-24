
using UnityEngine;
using UnityEngine.Events;

public class EnemyHelicopterTargeting : MonoBehaviour
{
    // =========================================================
    // TARGET
    // =========================================================

    [Header("Target")]
    [Tooltip("Optional reference to the enemy movement AI. Uses its target automatically.")]
    [SerializeField] private EnemyHelicopterAI movementAI;

    [Tooltip("Used if no movement AI is assigned, or AI target mode is disabled.")]
    [SerializeField] private Transform target;

    [SerializeField] private bool useMovementAITarget = true;

    // =========================================================
    // DETECTION
    // =========================================================

    [Header("Detection")]
    [SerializeField] private float detectionRange = 100f;

    [Tooltip("Total angle of the detection cone. 60 = 30 degrees on each side.")]
    [Range(1f, 360f)]
    [SerializeField] private float detectionAngle = 60f;

    [Tooltip("Check whether obstacles block the target.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Tooltip("Layers that can block line of sight. Exclude the enemy and player layers.")]
    [SerializeField] private LayerMask obstructionMask = ~0;

    [Tooltip("Height offset for the detection origin.")]
    [SerializeField] private float detectionHeight = 0f;

    // =========================================================
    // MISSILES
    // =========================================================

    [Header("Missiles")]
    [SerializeField] private GameObject missilePrefab;

    [Tooltip("Position and rotation where missiles spawn.")]
    [SerializeField] private Transform missileFiringTip;

    [Tooltip("Total missiles available.")]
    [SerializeField] private int maxMissiles = 6;

    [Tooltip("Time between missile launches, in seconds.")]
    [SerializeField] private float fireRate = 2f;

    [Tooltip("Automatically fire whenever a valid target is detected.")]
    [SerializeField] private bool automaticFire = true;

    // =========================================================
    // EVENTS
    // =========================================================

    [Header("Events")]
    [Tooltip("Invoked when a target enters the detection cone.")]
    [SerializeField] private UnityEvent onTargetAcquired;

    [Tooltip("Invoked when the target is no longer detected.")]
    [SerializeField] private UnityEvent onTargetLost;

    [Tooltip("Invoked whenever a missile is successfully launched.")]
    [SerializeField] private UnityEvent onMissileFired;

    [Tooltip("Invoked when ammunition reaches zero.")]
    [SerializeField] private UnityEvent onOutOfMissiles;

    private HealthComponent _healthComponent;

    // =========================================================
    // STATE
    // =========================================================

    private int missilesRemaining;
    private float nextFireTime;

    private Transform trackedTarget;

    public bool IsTargeted => trackedTarget != null;

    public Transform CurrentTarget => trackedTarget;

    public int MissilesRemaining => missilesRemaining;

    public UnityEvent TargetAcquired => onTargetAcquired;
    public UnityEvent TargetLost => onTargetLost;
    public UnityEvent MissileFired => onMissileFired;
    public UnityEvent OutOfMissiles => onOutOfMissiles;

    // =========================================================
    // INITIALIZATION
    // =========================================================

    private void Awake()
    {
        if (movementAI == null)
            movementAI = GetComponent<EnemyHelicopterAI>();

        _healthComponent = GetComponent<HealthComponent>();
        missilesRemaining = Mathf.Max(0, maxMissiles);
    }

    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        Transform candidate = GetTargetReference();

        bool canSeeTarget =
            candidate != null && CanDetectTarget(candidate);

        UpdateTargetState(
            canSeeTarget ? candidate : null
        );

        if (automaticFire && IsTargeted && _healthComponent.CurrentHealth>0)
        {
            TryFireMissile();
        }
    }

    // =========================================================
    // TARGET REFERENCE
    // =========================================================

    private Transform GetTargetReference()
    {
        if (useMovementAITarget && movementAI != null)
            return movementAI.Target;

        return target;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        // Switch to manually assigned target mode.
        useMovementAITarget = false;
    }

    public void UseMovementAITarget()
    {
        useMovementAITarget = true;
    }

    // =========================================================
    // DETECTION
    // =========================================================

    private bool CanDetectTarget(Transform candidate)
    {
        Vector3 origin = transform.position +
                         Vector3.up * detectionHeight;

        Vector3 direction = candidate.position - origin;

        float distance = direction.magnitude;

        // Distance check.
        if (distance > detectionRange)
            return false;

        if (distance < 0.01f)
            return true;

        direction /= distance;

        // Detection cone check.
        float angle = Vector3.Angle(
            transform.forward,
            direction
        );

        if (angle > detectionAngle * 0.5f)
            return false;

        // Line of sight check.
        if (requireLineOfSight && IsBlocked(origin, direction, distance, candidate))
            return false;

        return true;
    }

    private bool IsBlocked(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Transform candidate)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            obstructionMask,
            QueryTriggerInteraction.Ignore
        );

        // Sort hits so the closest obstruction is checked first.
        System.Array.Sort(hits,
            (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.transform;

            // Ignore our own colliders.
            if (hitTransform == transform ||
                hitTransform.IsChildOf(transform))
                continue;

            // Ignore target colliders.
            if (hitTransform == candidate ||
                hitTransform.IsChildOf(candidate) ||
                candidate.IsChildOf(hitTransform))
                continue;

            // Something else is blocking the target.
            return true;
        }

        return false;
    }

    // =========================================================
    // TARGET EVENTS
    // =========================================================

    private void UpdateTargetState(Transform newTarget)
    {
        // Nothing changed.
        if (trackedTarget == newTarget)
            return;

        // We lost the previous target.
        if (trackedTarget != null)
        {
            onTargetLost?.Invoke();
        }

        trackedTarget = newTarget;

        // We acquired a new target.
        if (trackedTarget != null)
        {
            onTargetAcquired?.Invoke();
        }
    }

    // =========================================================
    // MISSILE FIRING
    // =========================================================

    public void TryFireMissile()
    {
        if (!IsTargeted)
            return;

        if (missilesRemaining <= 0)
            return;

        if (Time.time < nextFireTime)
            return;

        if (missilePrefab == null || missileFiringTip == null)
            return;

        HomingRocket rocketTemplate =
            missilePrefab.GetComponent<HomingRocket>();

        if (rocketTemplate == null)
        {
            Debug.LogError(
                "Missile prefab does not contain a HomingRocket component!",
                this
            );

            return;
        }

        // Create the missile.
        GameObject go = Instantiate(
            missilePrefab,
            missileFiringTip.position,
            missileFiringTip.rotation
        );

        HomingRocket rocket = go.GetComponent<HomingRocket>();

        // Assign target and owner, just like the player controller.
        rocket.SetTarget(trackedTarget);
        rocket.SetOwner(transform);

        // Consume ammunition.
        missilesRemaining--;

        // Set next firing time.
        nextFireTime = Time.time + Mathf.Max(0.01f, fireRate);

        onMissileFired?.Invoke();

        if (missilesRemaining <= 0)
        {
            onOutOfMissiles?.Invoke();
        }
    }

    // =========================================================
    // AMMUNITION MANAGEMENT
    // =========================================================

    public void AddMissiles(int amount)
    {
        missilesRemaining = Mathf.Clamp(
            missilesRemaining + amount,
            0,
            maxMissiles
        );
    }

    public void Reload()
    {
        missilesRemaining = maxMissiles;
    }

    public void SetMissileCount(int amount)
    {
        missilesRemaining = Mathf.Clamp(
            amount,
            0,
            maxMissiles
        );
    }

    // =========================================================
    // DEBUG GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position +
                         Vector3.up * detectionHeight;

        // Detection range.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, detectionRange);

        // Detection cone.
        float halfAngle = detectionAngle * 0.5f;

        Vector3 leftDirection =
            Quaternion.Euler(0f, -halfAngle, 0f) *
            transform.forward;

        Vector3 rightDirection =
            Quaternion.Euler(0f, halfAngle, 0f) *
            transform.forward;

        Gizmos.color = Color.green;

        Gizmos.DrawRay(
            origin,
            leftDirection * detectionRange
        );

        Gizmos.DrawRay(
            origin,
            rightDirection * detectionRange
        );

        // Target line.
        if (trackedTarget != null)
        {
            Gizmos.color = Color.red;

            Gizmos.DrawLine(
                origin,
                trackedTarget.position
            );
        }
    }
}
