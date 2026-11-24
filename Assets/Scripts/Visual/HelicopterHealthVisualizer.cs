using UnityEngine;

public class HelicopterHealthVisualizer : MonoBehaviour
{
    private HealthComponent _healthComponent;
    [Header("HealthValueGagues")]
    [SerializeField] private float _startSmokeThreshold = 0.3f;
    [Header("Particles")]
    [SerializeField] private ParticleSystem _smokeParticle;
    [SerializeField] private float _smokeRate = 10;
    [SerializeField] private ParticleSystem _explosion;
    private void Awake()
    {
        _healthComponent = GetComponent<HealthComponent>();
    }
    private void OnEnable()
    {
        _healthComponent.OnDamageEvent += _healthComponent_OnDamageEvent;
        _healthComponent.OnDeathEvent += _healthComponent_OnDeathEvent;
    }
    private void OnDisable()
    {
        _healthComponent.OnDamageEvent -= _healthComponent_OnDamageEvent;
        _healthComponent.OnDeathEvent -= _healthComponent_OnDeathEvent;
    }

    private void _healthComponent_OnDeathEvent()
    {
        ExplosionVisual();
        //StopSmoke();
    }

    private void _healthComponent_OnDamageEvent(float arg1)
    {
        float absHealth = _healthComponent.CurrentHealth / _healthComponent.MaxHealth;

        if (absHealth <= _startSmokeThreshold)
        {
            StartSmoke();
        }
    }
    private void StartSmoke()
    {
        var rate = _smokeParticle.emission;
        rate.rateOverTime = _smokeRate;
    }
    private void StopSmoke()
    {
        var rate = _smokeParticle.emission;
        rate.rateOverTime = 0;
    }
    private void ExplosionVisual()
    {
        _explosion.Play();
    }

}
