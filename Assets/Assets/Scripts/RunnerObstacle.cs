using UnityEngine;

public enum ObstacleKind
{
    Low,    // obstáculo bajo: hay que SALTAR
    High    // obstáculo alto: hay que AGACHARSE
}

/// <summary>Marca un objeto como obstáculo. RunnerPlayer le quita una vida al tocarlo.</summary>
public class RunnerObstacle : MonoBehaviour
{
    public ObstacleKind kind = ObstacleKind.Low;
}
