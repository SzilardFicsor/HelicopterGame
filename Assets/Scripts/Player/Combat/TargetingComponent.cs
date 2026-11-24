using System.Collections;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class TargetingComponent : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Camera _camera;
    [SerializeField] private Transform _gunBarrel;
    [SerializeField]private RectTransform rectTransform;

    public LayerMask _hitMask = ~0;
    [Header("Targeting")]
    public Transform TargetTransform;
    public bool HasTarget = false;
    private bool _currentTargetInSights = false;
    private Coroutine timerCoroutine;
    [SerializeField] private float _targetingRange = 100;
    [SerializeField] private float _targetingRadius = 5;
    [SerializeField] private float _targetLossTime = 2;


    public Transform GetCurrentTarget()
    {
        if(TargetTransform !=null)
        {
            return TargetTransform;
        }
        else
        {
            return null;
        }
    }
    public Ray AimingRay(Transform weaponBarrel)
    {
        // Ray from the centre of the camera
        Ray cameraRay = new Ray(
            _camera.transform.position,
            _camera.transform.forward
        );

        Vector3 targetPoint;

        // Find what the camera is looking at
        if (Physics.Raycast(cameraRay, out RaycastHit cameraHit, _targetingRange, _hitMask))
        {
            targetPoint = cameraHit.point;
        }
        else
        {
            // If nothing is hit, aim towards max distance
            targetPoint = cameraRay.GetPoint(_targetingRange);
        }

        // Calculate direction from gun tip to target
        Vector3 direction = (targetPoint - weaponBarrel.position).normalized;
        Debug.DrawRay(weaponBarrel.position, direction * _targetingRange, Color.yellow);

        return new Ray(weaponBarrel.position, direction);
    }

    private IEnumerator TimerRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        timerCoroutine = null;

        OnTimerFinished();
    }
    public void StartTargetLossTimer(float duration)
    {
        // Cancel the previous timer if it's running
        StopTargetLossTimer();

        // Start a fresh timer
        timerCoroutine = StartCoroutine(TimerRoutine(duration));
    }
    public void StopTargetLossTimer()
    {
        if (timerCoroutine != null)
        {
            StopCoroutine(timerCoroutine);
            timerCoroutine = null;
        }
    }
    private void OnTimerFinished()
    {
        ClearTarget();  
    }

    private void ClearTarget()
    {
        TargetTransform = null;
        HasTarget = false;
        Debug.Log("Target Lost!");
    }
    private void SetTarget(Transform target)
    {
        TargetTransform = target;
        HasTarget = true;
        Debug.Log($"Target Set:{TargetTransform.name}");
    }
    private void LateUpdate()
    {
        if (TargetTransform == null)
        {
           rectTransform.gameObject.SetActive(false);
            return;
        }

        Vector3 screenPosition =
            _camera.WorldToScreenPoint(TargetTransform.position);

        // Hide crosshair if target is behind the camera
        if (screenPosition.z <= 0)
        {
            rectTransform.gameObject.SetActive(false);
            return;
        }

        rectTransform.gameObject.SetActive(true);

        rectTransform.position = screenPosition;
    }

    private void Update()
    {
        Targeting(_gunBarrel);
    }

    public void Targeting(Transform weaponBarrel)
    {
        if (Physics.SphereCast(AimingRay(weaponBarrel), _targetingRadius, out RaycastHit hit, _targetingRange, _hitMask))
        {
            HealthComponent health = hit.transform.GetComponentInParent<HealthComponent>();
            if (health != null)
            {
                if (health.transform == TargetTransform)
                {
                    StopTargetLossTimer();
                    _currentTargetInSights = true;
                }
                else if(timerCoroutine == null)
                {
                    _currentTargetInSights = false;
                    StartTargetLossTimer(_targetLossTime);
                }

                if (!HasTarget || health.transform != TargetTransform)
                {
                    StopTargetLossTimer();
                    _currentTargetInSights = true;
                    SetTarget(health.transform);
                }



            }
            else
            {
                Debug.Log("NO TARGET");
                if (timerCoroutine == null && HasTarget)
                {
                    _currentTargetInSights = false;
                    StartTargetLossTimer(_targetLossTime);
                }
            }
        }
    }
}
