using System.IO;
using DoctorWho.Planets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.Planets.Editor
{
    // Automatic install-on-compile disabled: use the menu command manually.
    internal static class PlanetV2Installer
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string SettingsPath = "Assets/PlanetSystem/Settings/DefaultPlanetGenerationSettings.asset";
        private const string MaterialFolder = "Assets/PlanetSystem/Materials";
        private const string TerrainMaterialPath = MaterialFolder + "/PlanetV2Terrain.mat";
        private const string OceanMaterialPath = MaterialFolder + "/PlanetV2Ocean.mat";
        private const string AtmosphereMaterialPath = MaterialFolder + "/PlanetV2Atmosphere.mat";

        // Intentionally no static constructor. Installation is manual to prevent editor stalls.

        [MenuItem("Tools/Doctor Who/Planet V2/Install Or Repair")]
        private static void Install()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath)) return;
            EnsureFolder(MaterialFolder);

            PlanetGenerationSettings settings = AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(SettingsPath);
            if (settings == null) return;
            settings.radius = Mathf.Max(settings.radius, 1200f);
            settings.maxTerrainHeight = Mathf.Max(settings.maxTerrainHeight, 260f);
            settings.patchResolution = Mathf.Max(settings.patchResolution, 24);
            settings.maxLod = Mathf.Max(settings.maxLod, 7);

            Material terrain = GetOrCreateMaterial(TerrainMaterialPath, "DoctorWho/PlanetV2Terrain");
            Material ocean = GetOrCreateMaterial(OceanMaterialPath, "DoctorWho/PlanetV2Ocean");
            Material atmosphere = GetOrCreateMaterial(AtmosphereMaterialPath, "DoctorWho/PlanetV2Atmosphere");
            if (terrain == null || ocean == null || atmosphere == null) { EditorApplication.delayCall += Install; return; }

            Scene current = SceneManager.GetActiveScene();
            bool alreadyOpen = current.path == ScenePath;
            Scene scene = alreadyOpen ? current : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            GameObject systems = null;
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "Planet Systems") systems = root;
            if (systems == null)
            {
                systems = new GameObject("Planet Systems");
                SceneManager.MoveGameObjectToScene(systems, scene);
            }

            Transform runtime = systems.transform.Find("Planet Runtime");
            if (runtime == null)
            {
                GameObject go = new GameObject("Planet Runtime");
                go.transform.SetParent(systems.transform, false);
                runtime = go.transform;
            }

            Transform player = systems.transform.Find("Planet Player");
            if (player == null)
            {
                GameObject go = new GameObject("Planet Player");
                go.transform.SetParent(systems.transform, false);
                player = go.transform;
            }

            CharacterController oldCharacter = player.GetComponent<CharacterController>();
            if (oldCharacter != null) Object.DestroyImmediate(oldCharacter);

            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = player.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = .36f;
            capsule.center = new Vector3(0f, .9f, 0f);

            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body == null) body = player.gameObject.AddComponent<Rigidbody>();
            body.mass = 78f;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            Transform pivot = player.Find("Camera Pivot");
            if (pivot == null)
            {
                pivot = new GameObject("Camera Pivot").transform;
                pivot.SetParent(player, false);
            }
            pivot.localPosition = new Vector3(0f, 1.65f, 0f);

            Camera camera = pivot.GetComponentInChildren<Camera>();
            if (camera == null)
            {
                GameObject cameraGo = new GameObject("Player Camera");
                cameraGo.tag = "MainCamera";
                cameraGo.transform.SetParent(pivot, false);
                camera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }
            camera.nearClipPlane = settings.cameraNearClip;
            camera.fieldOfView = settings.cameraFov;
            camera.farClipPlane = Mathf.Max(20000f, settings.radius * 12f);
            camera.allowHDR = true;

            PlanetPrototypeGenerator generator = runtime.GetComponent<PlanetPrototypeGenerator>();
            if (generator == null) generator = runtime.gameObject.AddComponent<PlanetPrototypeGenerator>();
            generator.ConfigureV2(settings, terrain, ocean, atmosphere, camera.transform);

            RadialFirstPersonController controller = player.GetComponent<RadialFirstPersonController>();
            if (controller == null) controller = player.gameObject.AddComponent<RadialFirstPersonController>();
            controller.Configure(runtime, pivot, settings);

            PlanetFloatingOrigin floatingOrigin = systems.GetComponent<PlanetFloatingOrigin>();
            if (floatingOrigin == null) floatingOrigin = systems.AddComponent<PlanetFloatingOrigin>();
            floatingOrigin.Configure(player, settings);

            PlanetRuntimeRoot runtimeRoot = runtime.GetComponent<PlanetRuntimeRoot>();
            if (runtimeRoot != null) runtimeRoot.Configure(settings, player);

            player.position = runtime.position + new Vector3(.27f, .93f, .24f).normalized * (settings.radius + settings.maxTerrainHeight + 30f);
            generator.SetObserver(camera.transform); // Generation is streamed by Update/Play Mode.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[Planet V2] Quadtree LOD planet, streamed colliders, climate terrain, stable radial player, ocean, atmosphere, and floating origin installed.");
        }

        [MenuItem("Tools/Doctor Who/Planet V2/Regenerate")]
        private static void Regenerate()
        {
            PlanetPrototypeGenerator generator = Object.FindObjectOfType<PlanetPrototypeGenerator>();
            if (generator != null) generator.Regenerate();
        }

        [MenuItem("Tools/Doctor Who/Planet V2/Respawn Player")]
        private static void RespawnPlayer()
        {
            RadialFirstPersonController controller = Object.FindObjectOfType<RadialFirstPersonController>();
            if (controller != null) controller.RespawnToSafeSurface();
        }

        [MenuItem("Tools/Doctor Who/Planet V2/Frame Planet")]
        private static void FramePlanet()
        {
            PlanetPrototypeGenerator generator = Object.FindObjectOfType<PlanetPrototypeGenerator>();
            if (generator == null) return;
            Selection.activeGameObject = generator.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.size = generator.Settings.radius * 1.65f;
                SceneView.lastActiveSceneView.Repaint();
            }
        }

        private static Material GetOrCreateMaterial(string path, string shaderName)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return null;
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}

