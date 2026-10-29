using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // Arrastra a Cheems aquí
    public Vector3 offset = new Vector3(0f, 3.5f, -6f); // Distancia de la cámara respecto a Cheems
    public float smoothSpeed = 10f;

    void LateUpdate()
    {
        if (target == null) return;

        // Posición deseada de la cámara detrás del personaje
        Vector3 desiredPosition = target.position + offset;

        // Movimiento suave hacia la posición deseada
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.position = smoothedPosition;

        // Hacer que la cámara mire ligeramente hacia Cheems
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }
}