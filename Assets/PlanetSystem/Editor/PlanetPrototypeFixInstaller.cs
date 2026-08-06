using System.IO;
using DoctorWho.Planets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.Planets.Editor
{
    [InitializeOnLoad]
    internal static class PlanetPrototypeFixInstaller
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string SettingsPath = "Assets/PlanetSystem/Settings/DefaultPlanetGenerationSettings.asset";

        static PlanetPrototypeFixInstaller() => EditorApplication.delayCall += Install;

        [MenuItem("Tools/Doctor Who/Apply Planet Movement and Detail Fix")]
        private static void Install()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath)) return;

            PlanetGenerationSettings settings = AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(SettingsPath);
            if (settings == null) return;
            settings.faceResolution = Mathf.Max(settings.faceResolution, 72);
            settings.maxTerrainHeight = Mathf.Max(settings.maxTerrainHeight, 145f);

            Scene current = SceneManager.GetActiveScene();
            bool alreadyOpen = current.path == ScenePath;
            Scene scene = alreadyOpen ? current : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            GameObject systems = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "Planet Systems") systems = root;
            if (systems == null) return;

            Transform runtime = systems.transform.Find("Planet Runtime");
            Transform player = systems.transform.Find("Planet Player");
            if (runtime == null || player == null) return;

            PlanetPrototypeGenerator generator = runtime.GetComponent<PlanetPrototypeGenerator>();
            if (generator != null) generator.Regenerate();

            CharacterController oldController = player.GetComponent<CharacterController>();
            if (oldController != null) Object.DestroyImmediate(oldController);

            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = player.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = .38f;
            capsule.center = new Vector3(0f, .9f, 0f);

            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body == null) body = player.gameObject.AddComponent<Rigidbody>();
            body.mass = 75f;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            PhysicMaterial frictionless = new PhysicMaterial("Planet Player Frictionless")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            capsule.material = frictionless;

            Transform pivot = player.Find("Camera Pivot");
            RadialFirstPersonController controller = player.GetComponent<RadialFirstPersonController>();
            if (controller == null) controller = player.gameObject.AddComponent<RadialFirstPersonController>();
            controller.Configure(runtime, pivot, settings);

            if (generator != null)
            {
                Vector3 spawnDirection = new Vector3(.22f, .96f, .16f).normalized;
                float spawnRadius = generator.SurfaceRadius(spawnDirection) + 2.2f;
                player.position = runtime.position + spawnDirection * spawnRadius;
                player.rotation = Quaternion.FromToRotation(Vector3.up, spawnDirection);
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            PlanetRuntimeRoot rootComponent = runtime.GetComponent<PlanetRuntimeRoot>();
            if (rootComponent != null) rootComponent.Configure(settings, player);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);

            Debug.Log("[Planet Fix] Seamless collision, Rigidbody radial movement, accurate spawning, and richer terrain are installed.");
        }

        [MenuItem("Tools/Doctor Who/Frame Planet In Scene View")]
        private static void FramePlanet()
        {
            PlanetPrototypeGenerator generator = Object.FindObjectOfType<PlanetPrototypeGenerator>();
            if (generator == null)
            {
                Debug.LogWarning("Open PlanetDevelopment first, then run Frame Planet In Scene View.");
                return;
            }

            Selection.activeGameObject = generator.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            if (SceneView.lastActiveSceneView != null)
            {
                float size = generator.Settings != null ? generator.Settings.radius * 1.45f : 750f;
                SceneView.lastActiveSceneView.size = size;
                SceneView.lastActiveSceneView.Repaint();
            }
        }
    }
}
