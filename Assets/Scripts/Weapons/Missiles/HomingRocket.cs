using UnityEngine;

/// <summary>
/// Guided homing rocket. Spawn it, call SetTarget(), and it flies itself.
/// No Rigidbody needed: it moves by transform and uses a raycast sweep
/// each frame so fast rockets can't tunnel through thin colliders.
/// </summary>
public class HomingRocket : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField] private float speed = 60f;
    [SerializeField] private float turnRate = 120f;        // degrees per second
    [SerializeField] private float guidanceDelay = 0.3f;   // flies straight first, then starts homing
    [SerializeField] private float lifetime = 8f;

    [Header("Damage")]
    [SerializeField] private float damage = 50f;
    [SerializeField] private LayerMask hitMask = ~0;       // what the rocket can hit
    [SerializeField] private GameObject explosionPrefab;   // optional VFX

    private Transform target;
    private Transform owner;   // so the rocket ignores your own helicopter
    private float age;
    private bool exploded;

    /// <summary>Set (or change) the target at any time, including mid-flight.</summary>
    public void SetTarget(Transform newTarget) => target = newTarget;

    /// <summary>Optional: pass your helicopter so the rocket never hits it.</summary>
    public void SetOwner(Transform newOwner) => owner = newOwner;

    private void Start()
    {
        //AudioManager.instance.PlayOneShot(FMODEvents.instance._missile_Launch,transform.position);
    }
    private void Update()
    {
        if (exploded) return;

        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Explode(transform.position);
            return;
        }

        // Steer toward the target after the guidance delay
        if (target != null && age >= guidanceDelay)
        {
            Vector3 toTarget = target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(toTarget.normalized);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, desired, turnRate * Time.deltaTime);
            }
        }

        // Move forward, sweeping for hits along the way
        float step = speed * Time.deltaTime;
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit,
                step, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (owner == null || !hit.collider.transform.IsChildOf(owner))
            {
                transform.position = hit.point;
                OnHit(hit.collider, hit.point);
                return;
            }
        }

        transform.position += transform.forward * step;
    }

    private void OnHit(Collider other, Vector3 point)
    {
        //AudioManager.instance.PlayOneShot(FMODEvents.instance._missile_Explosion, transform.position);
        HealthComponent health = other.GetComponentInParent<HealthComponent>();
        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        if (health != null)
        {
            health.Damage(damage);
        }
        if(rb != null)
        {
            Vector3 dir = other.transform.position - transform.position;
            dir.Normalize();
            rb.AddForce(dir * 100,ForceMode.Impulse);
        }
        // ===============================================

        Explode(point);
    }

    private void Explode(Vector3 position)
    {
        //AudioManager.instance.PlayOneShot(FMODEvents.instance._explosion_01,transform.position);
        if (exploded) return;
        exploded = true;

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, position, Quaternion.identity);

        Destroy(gameObject);
    }
}
