#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RunnerSetupTool
{
    [MenuItem("Tools/Runner/Configurar escena (todo en uno)")]
    public static void ConfigureScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        GameObject model = GameObject.Find("CheemsVisual");
        if (model == null) model = GameObject.Find("Cheems");
        if (model == null)
        {
            EditorUtility.DisplayDialog("Runner", "No encontré un objeto llamado 'Cheems' o 'CheemsVisual' en la escena. Arrastra el modelo a la escena y vuelve a intentarlo.", "Aceptar");
            return;
        }

        GameObject playerObject = GameObject.Find("CheemsPlayer");
        RunnerPlayer player;
        if (playerObject == null)
        {
            Bounds bounds = CalculateBounds(model);
            Vector3 oldWorldPosition = model.transform.position;
            playerObject = new GameObject("CheemsPlayer");
            playerObject.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            playerObject.transform.rotation = Quaternion.identity;
            model.transform.SetParent(playerObject.transform, true);
            model.transform.position = oldWorldPosition;
        }
        else if (model.transform.parent != playerObject.transform)
        {
            model.transform.SetParent(playerObject.transform, true);
        }

        playerObject.tag = "Player";
        playerObject.transform.localScale = Vector3.one;
        Rigidbody rb = GetOrAdd<Rigidbody>(playerObject);
        rb.mass = 1f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(playerObject);
        capsule.direction = 1;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.45f;

        player = GetOrAdd<RunnerPlayer>(playerObject);
        player.visual = model.transform;
        player.runOnStart = false;
        player.autoFitCollider = true;
        player.handleHitsHere = true;

        GameObject scenarioObject = GameObject.Find("Scenario");
        if (scenarioObject == null) scenarioObject = new GameObject("Scenario");
        scenarioObject.transform.position = Vector3.zero;
        scenarioObject.transform.rotation = Quaternion.identity;
        scenarioObject.transform.localScale = Vector3.one;
        ScenarioGenerator scenario = GetOrAdd<ScenarioGenerator>(scenarioObject);
        scenario.player = playerObject.transform;

        GameObject managerObject = GameObject.Find("GameManager");
        if (managerObject == null) managerObject = new GameObject("GameManager");
        RunnerGameManager manager = GetOrAdd<RunnerGameManager>(managerObject);
        manager.player = player;
        manager.scenario = scenario;

        ObstacleSpawner spawner = GetOrAdd<ObstacleSpawner>(managerObject);
        spawner.player = player;
        spawner.scenario = scenario;
        manager.spawner = spawner;

        // Remove duplicate player controllers accidentally left on the model.
        RunnerPlayer[] controllers = model.GetComponents<RunnerPlayer>();
        foreach (RunnerPlayer controller in controllers) Object.DestroyImmediate(controller);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = playerObject;
        EditorUtility.DisplayDialog("Runner configurado", "Se configuraron CheemsPlayer, Scenario, GameManager y ObstacleSpawner. Revisa la cámara antes de pulsar Play: este paquete no incluye un script de seguimiento de cámara.", "Aceptar");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    static Bounds CalculateBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, new Vector3(1f, 2f, 1f));
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
#endif
