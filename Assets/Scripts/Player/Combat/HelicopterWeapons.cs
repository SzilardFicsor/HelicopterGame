using UnityEngine;
using UnityEngine.SceneManagement;

public class HelicopterWeapons : MonoBehaviour
{

    [SerializeField] private Transform _gunFiringTip;
    [SerializeField] private Transform _missileFiringTip;
    private bool _mainGunFiring = false;

    [SerializeField] private Camera _camera;
    public LayerMask hitMask = ~0;
    [Header("Main Weapon")]
    [SerializeField] private float _mainRPM = 600f;
    [SerializeField] private float _range = 100;

    [Header("Secondary Weapon")]
    [SerializeField] private GameObject _rocketPrefab;
    [SerializeField] private TargetingComponent _targetingComponent;
    [SerializeField] private float _missileFireRate = 2;
    private float _nextMissileFired = 0;
    private bool _firedMissile = false;

    private float _nextFireTime;
    private float _fireInterval => 60f / _mainRPM;
    public PlayerInput _inputActions;

    private void Awake()
    {
        _inputActions = new PlayerInput();
        _targetingComponent = gameObject.GetComponent<TargetingComponent>();
    }

    private void OnEnable()
    {
        _inputActions.Enable();
        _inputActions.Player.Attack_Primary.performed += Attack_performed;
        _inputActions.Player.Attack_Primary.canceled += Attack_canceled;
        _inputActions.Player.Attack_Secondary.performed += AttackSecondary_performed;
    }

    private void Attack_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        _mainGunFiring = true;
    }
    private void Attack_canceled(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        _mainGunFiring = false;
    }

    private void AttackSecondary_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        if (Time.time >= _nextMissileFired)
        {
            FireMissle();
        }
    }

    private void FireMissle()
    {
        _nextMissileFired = Time.time + _missileFireRate;
        GameObject go = Instantiate(_rocketPrefab, _missileFiringTip.position, _missileFiringTip.rotation);
        HomingRocket rocket = go.GetComponent<HomingRocket>();
        rocket.SetTarget(_targetingComponent.GetCurrentTarget());   // any Transform
        rocket.SetOwner(transform);


    }


    private void OnDisable()
    {
        _inputActions.Player.Attack_Primary.performed -= Attack_performed;
        _inputActions.Player.Attack_Primary.canceled -= Attack_canceled;
        _inputActions.Player.Attack_Secondary.performed -= AttackSecondary_performed;
        _inputActions.Disable();
    }

    private void Update()
    {
        //if (Time.time >= _nextMissileFired)
        //{
        //    _nextMissileFired += Time.time + _missileFireRate;
        //}


        GatlingMainWeapon();
    }
    private void GatlingMainWeapon()
    {
        if (_mainGunFiring && Time.time >= _nextFireTime)
        {
            FireMainWeapon();
            _nextFireTime += _fireInterval;

            if (_nextFireTime < Time.time)
            {
                _nextFireTime = Time.time + _fireInterval;
            }
        }
    }

    private Ray AimingRay()
    {
        // Ray from the centre of the camera
        Ray cameraRay = new Ray(
            _camera.transform.position,
            _camera.transform.forward
        );

        Vector3 targetPoint;

        // Find what the camera is looking at
        if (Physics.Raycast(cameraRay, out RaycastHit cameraHit, _range, hitMask))
        {
            targetPoint = cameraHit.point;
        }
        else
        {
            // If nothing is hit, aim towards max distance
            targetPoint = cameraRay.GetPoint(_range);
        }

        // Calculate direction from gun tip to target
        Vector3 direction = (targetPoint - _gunFiringTip.position).normalized;
        Debug.DrawRay(_gunFiringTip.position, direction * _range, Color.red);

        return new Ray(_gunFiringTip.position, direction);
    }
    private void FireMainWeapon()
    {
        RaycastHit hit;
        Ray ray = AimingRay();
        if (Physics.Raycast(ray, out hit, _range, hitMask))
        {
            Debug.Log(hit.transform.gameObject.name);
            HealthComponent comp = hit.transform.GetComponentInParent<HealthComponent>();
            if (comp != null)
            {
                comp.Damage(100);
            }
        }
    }

    private void FireSecondaryWeapon()
    {

    }

}
