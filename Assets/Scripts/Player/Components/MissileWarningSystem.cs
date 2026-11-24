using UnityEngine;
using System.Collections;

public class MissileWarningSystem : MonoBehaviour
{
    //[Header("FMOD")]
    //[SerializeField] private EventReference warningEvent;
    //[SerializeField] private string parameterName = "HomingDistance";

    [Header("Distance mapping")]
    [Tooltip("Missiles farther than this are ignored (parameter = 0).")]
    [SerializeField] private float maxDistance = 150f;
    [Tooltip("At or below this distance the parameter is at its maximum.")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("ON: 1 = missile is close, 0 = far. OFF: 0 = close, 1 = far.")]
    [SerializeField] private bool closerIsHigher = true;

    [Header("Behaviour")]
    [SerializeField] private string missileTag = "EnemyMissile";
    [SerializeField] private float checkInterval = 0.1f;
    [Tooltip("How fast the parameter moves toward its target (units per second).")]
    [SerializeField] private float smoothing = 4f;

    [Header("UI")]
    [SerializeField] private GameObject _missileImboundUI;
    [SerializeField] private float onTime = 0.3f;
    [SerializeField] private float offTime = 0.3f;

    private Coroutine blinkRoutine;


    //private EventInstance instance;
    private float currentValue;
    private float targetValue;
    private float nextCheckTime;
    private bool warningActive;

    void Start()
    {
        //instance = RuntimeManager.CreateInstance(warningEvent);
        _missileImboundUI.SetActive(false);
    }

    void Update()
    {
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            Scan();
        }

        if (!warningActive) return;

        currentValue = Mathf.MoveTowards(currentValue, targetValue, smoothing * Time.deltaTime);
        //instance.setParameterByName(parameterName, currentValue);
    }
    public void StartBlinking()
    {
        if (blinkRoutine != null) return; // already blinking
        blinkRoutine = StartCoroutine(BlinkRoutine());
    }

    public void StopBlinking()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }
        _missileImboundUI.SetActive(false);
    }

    // Lets you change the speed at runtime, e.g. faster as the missile gets closer
    public void SetBlinkSpeed(float on, float off)
    {
        onTime = on;
        offTime = off;
    }

    private IEnumerator BlinkRoutine()
    {
        while (true)
        {
            _missileImboundUI.SetActive(true);
            yield return new WaitForSeconds(onTime);
            _missileImboundUI.SetActive(false);
            yield return new WaitForSeconds(offTime);
        }
    }

    void OnDisable()
    {
        blinkRoutine = null;
    }

    private void Scan()
    {
        GameObject[] missiles = GameObject.FindGameObjectsWithTag(missileTag);

        float closest = float.MaxValue;
        foreach (GameObject m in missiles)
        {
            float d = Vector3.Distance(transform.position, m.transform.position);
            if (d < closest) closest = d;
        }

        bool missileInRange = closest <= maxDistance;

        if (missileInRange)
        {
            // 0 at maxDistance, 1 at minDistance
            float t = Mathf.InverseLerp(maxDistance, minDistance, closest);
            targetValue = closerIsHigher ? t : 1f - t;

            if (!warningActive) StartWarning();
        }
        else if (warningActive)
        {
            StopWarning();
        }
    }

    private void StartWarning()
    {

        StartBlinking();
        warningActive = true;
        currentValue = targetValue; // avoid sweeping through values on start
        //instance.setParameterByName(parameterName, currentValue);
        //instance.start();
    }

    private void StopWarning()
    {
        StopBlinking();
        warningActive = false;
        //instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    void OnDestroy()
    {
        //if (instance.isValid())
        //{
        //    instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        //    instance.release();
        //}
    }
}