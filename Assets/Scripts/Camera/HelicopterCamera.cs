using UnityEngine;

/// <summary>
/// Third-person camera for the HelicopterController.
/// The MOUSE aims the camera (it reads AimYaw / AimPitch from the controller), so looking
/// around is instant and crisp. The camera orbits a point that smoothly follows the
/// helicopter, so the heli's movement still feels floaty and the camera never feels stiff.
/// Put this on the Main Camera. Do NOT parent the camera to the helicopter.
/// </summary>
[RequireComponent(typeof(Camera))]
public class HelicopterCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private HelicopterController helicopter;

    [Header("Framing")]
    [Tooltip("Camera position relative to the follow point, in aim space (x right, y up, z back is negative).")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 3.5f, -10f);
    [Tooltip("Point the camera looks at, relative to the heli.")]
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("The camera looks this far ahead of the heli along the aim direction.")]
    [SerializeField] private float lookAheadDistance = 12f;

    [Header("Follow (how the camera trails the heli's movement)")]
    [Tooltip("Higher = the camera trails further behind when the heli moves. Lower = tighter.")]
    [SerializeField] private float followSmoothTime = 0.12f;
    [Tooltip("Seconds of heli velocity added to the follow point. Reduces trailing at high speed.")]
    [SerializeField] private float velocityLead = 0.05f;

    [Header("Mouse look")]
    [Tooltip("Tiny filter on camera rotation to remove mouse jitter. Higher = more responsive. 0 = none.")]
    [SerializeField] private float rotationSharpness = 40f;

    [Header("Speed FOV kick")]
    [SerializeField] private float speedFovKick = 8f;
    [Tooltip("Speed (m/s) at which the full FOV kick is reached.")]
    [SerializeField] private float referenceSpeed = 25f;
    [SerializeField] private float fovSharpness = 4f;

    [Header("Collision (optional)")]
    [Tooltip("Layers the camera should not go through (ground, buildings). Leave empty to disable. Do NOT include the helicopter's layer.")]
    [SerializeField] private LayerMask collisionMask = 0;
    [SerializeField] private float collisionPadding = 0.4f;

    private Camera cam;
    private float baseFov;
    private Vector3 followPoint, followVelocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;
    }

    private void Start()
    {
        SnapToTarget();
    }

    private void OnValidate()
    {
        followSmoothTime = Mathf.Max(0.001f, followSmoothTime);
    }

    /// <summary>Instantly places the camera at its ideal position (no swoop-in on start).</summary>
    public void SnapToTarget()
    {
        if (helicopter == null) return;

        followPoint = helicopter.transform.position;
        followVelocity = Vector3.zero;

        ComputePose(out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    private void LateUpdate()
    {
        if (helicopter == null) return;

        float dt = Time.deltaTime;

        // The follow point trails the heli smoothly (this is where the "floaty chase" comes from)
        Vector3 target = helicopter.transform.position + helicopter.Velocity * velocityLead;
        followPoint = Vector3.SmoothDamp(followPoint, target, ref followVelocity, followSmoothTime);

        ComputePose(out Vector3 pos, out Quaternion rot);

        transform.position = pos;
        if (rotationSharpness > 0f)
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-rotationSharpness * dt));
        else
            transform.rotation = rot;

        UpdateFov(dt);
    }


    // ---------------------------------------------------------------- HELPERS
    private void ComputePose(out Vector3 position, out Quaternion rotation)
    {
        // Camera orientation comes straight from the mouse aim (no lag).
        Quaternion yawRot = Quaternion.Euler(0f, helicopter.AimYaw, 0f);
        Quaternion orbit = Quaternion.Euler(-helicopter.AimPitch, helicopter.AimYaw, 0f);

        Vector3 pivot = followPoint + yawRot * lookAtOffset;
        position = followPoint + orbit * offset;

        if (collisionMask.value != 0)
        {
            // Cast from the real heli position so the camera can't be pushed through walls behind it
            Vector3 origin = helicopter.transform.position + yawRot * lookAtOffset;
            if (Physics.Linecast(origin, position, out RaycastHit hit, collisionMask, QueryTriggerInteraction.Ignore))
            {
                position = hit.point + hit.normal * collisionPadding;
            }
        }

        Vector3 lookPoint = pivot + orbit * Vector3.forward * lookAheadDistance;
        Vector3 toLook = lookPoint - position;
        rotation = toLook.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toLook, Vector3.up) : transform.rotation;
    }

    private void UpdateFov(float dt)
    {
        float speed01 = Mathf.Clamp01(helicopter.Velocity.magnitude / Mathf.Max(referenceSpeed, 0.01f));
        float targetFov = baseFov + speedFovKick * speed01;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovSharpness * dt));
    }
}