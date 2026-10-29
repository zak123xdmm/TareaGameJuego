using UnityEngine;

/// <summary>Hamburguesa recogible: gira y flota. Da puntos al tocarla.</summary>
public class RunnerPickup : MonoBehaviour
{
    public int points = 50;
    public float spinSpeed = 180f;
    public float bobAmount = 0.12f;

    [HideInInspector] public bool collected;

    Vector3 startPos;
    float phase;

    void Start()
    {
        startPos = transform.position;
        phase = Random.value * 6.2831f;
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        Vector3 p = startPos;
        p.y += Mathf.Sin(Time.time * 3f + phase) * bobAmount;
        transform.position = p;
    }

    public void Collect()
    {
        if (collected) return;
        collected = true;
        Destroy(gameObject);
    }
}
