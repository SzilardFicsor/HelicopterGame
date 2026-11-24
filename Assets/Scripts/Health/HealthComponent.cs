using System;
using System.Collections;
using UnityEngine;

public class HealthComponent : MonoBehaviour
{
    public float MaxHealth = 200;
    public float CurrentHealth = 100;
    public float AbsoluteHealth;
    public float DeathCountDown;
    public event Action<float> OnDamageEvent;
    public event Action<float> OnDamageEventUI;
    public event Action<float> OnBlockedAttackEvent;
    public event Action<float> OnHealEvent;
    public event Action OnDeathEvent;


    private void OnEnable()
    {
        OnDamageEvent += HealthComponent_OnDamageEvent;
        OnHealEvent += HealthComponent_OnHealEvent;
        OnDeathEvent += HealthComponent_OnDeathEvent;
    }

    private void HealthComponent_OnDeathEvent()
    {
        StartCoroutine(StartDeathCountDown(DeathCountDown));
    }
    private IEnumerator StartDeathCountDown(float countdown)
    {
        yield return new WaitForSeconds(countdown);
        if (gameObject.tag != "Player")
        {
            Destroy(gameObject);
        }

    }

    private void HealthComponent_OnHealEvent(float obj)
    {
        CurrentHealth += obj;
    }

    private void OnDisable()
    {
        OnDamageEvent -= HealthComponent_OnDamageEvent;
    }

    private void HealthComponent_OnDamageEvent(float obj)
    {
        CurrentHealth -= obj;

        if (CurrentHealth <= 0)
        {
            OnDeathEvent?.Invoke();
        }
        OnDamageEventUI?.Invoke(obj);
    }

    public void Damage(float damage)
    {

                OnDamageEvent?.Invoke(damage);

               

    }
    //private IEnumerator DealDamageOverTime(float damage, float duration)
    //{
    //    _startedFireAttack = false;
    //    float elapsed = 0;
    //    float damagePerSecond = damage / duration;
    //    _fireDamageParticle.SetActive(true);

    //    for (elapsed = 0; elapsed < duration; elapsed += 1f)
    //    {
    //        _fireDamageParticle.SetActive(true);
    //        OnDamageEvent?.Invoke(damagePerSecond, DamageType.Fire);
    //        if (elapsed == duration - 1)
    //        {
    //            if (!_startedFireAttack)
    //            {
    //                _fireDamageParticle.SetActive(false);
    //            }
    //            _startedFireAttack = false;
    //        }
    //        yield return new WaitForSeconds(1f); // The time between ticks
    //    }
    //}
    public void Heal(float heal)
    {
        OnHealEvent?.Invoke(heal);
    }
}
