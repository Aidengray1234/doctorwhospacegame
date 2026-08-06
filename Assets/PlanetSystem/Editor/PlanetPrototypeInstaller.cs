using System.IO;
using DoctorWho.Planets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.Planets.Editor
{
    [InitializeOnLoad]
    internal static class PlanetPrototypeInstaller
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string SettingsPath = "Assets/PlanetSystem/Settings/DefaultPlanetGenerationSettings.asset";
        private const string MaterialFolder = "Assets/PlanetSystem/Materials";
        private const string TerrainMaterialPath = MaterialFolder + "/PlanetTerrain.mat";
        private const string OceanMaterialPath = MaterialFolder + "/PlanetOcean.mat";

        static PlanetPrototypeInstaller() => EditorApplication.delayCall += Install;

        [MenuItem("Tools/Doctor Who/Install First Planet Prototype")]
        private static void Install()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) return;
            EnsureFolder(MaterialFolder);

            PlanetGenerationSettings settings = AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(SettingsPath);
            if (settings == null) return;

            Material terrain = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (terrain == null)
            {
                Shader shader = Shader.Find("DoctorWho/PlanetVertexColor");
                if (shader == null) { EditorApplication.delayCall += Install; return; }
                terrain = new Material(shader) { name = "PlanetTerrain" };
                AssetDatabase.CreateAsset(terrain, TerrainMaterialPath);
            }

            Material ocean = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (ocean == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                ocean = new Material(shader) { name = "PlanetOcean", color = new Color(.03f, .22f, .52f, 1f) };
                ocean.SetFloat("_Smoothness", .85f);
                AssetDatabase.CreateAsset(ocean, OceanMaterialPath);
            }

            Scene current = SceneManager.GetActiveScene();
            bool alreadyOpen = current.path == ScenePath;
            Scene scene = alreadyOpen ? current : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject systems = GameObject.Find("Planet Systems");
            if (systems == null || systems.scene != scene)
            {
                foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "Planet Systems") systems = root;
            }
            if (systems == null) return;

            Transform runtime = systems.transform.Find("Planet Runtime");
            if (runtime == null)
            {
                var go = new GameObject("Planet Runtime");
                go.transform.SetParent(systems.transform, false);
                runtime = go.transform;
            }

            PlanetPrototypeGenerator generator = runtime.GetComponent<PlanetPrototypeGenerator>();
            if (generator == null) generator = runtime.gameObject.AddComponent<PlanetPrototypeGenerator>();
            generator.Configure(settings, terrain, ocean);
            generator.Regenerate();

            Transform player = systems.transform.Find("Planet Player");
            if (player == null)
            {
                var playerGo = new GameObject("Planet Player");
                playerGo.transform.SetParent(systems.transform, false);
                playerGo.transform.position = runtime.position + Vector3.up * (settings.radius + settings.maxTerrainHeight + 8f);
                var cc = playerGo.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = .38f; cc.center = new Vector3(0f, .9f, 0f);

                var pivot = new GameObject("Camera Pivot").transform;
                pivot.SetParent(playerGo.transform, false);
                pivot.localPosition = new Vector3(0f, 1.65f, 0f);

                var cameraGo = new GameObject("Player Camera");
                cameraGo.tag = "MainCamera";
                cameraGo.transform.SetParent(pivot, false);
                cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();

                var controller = playerGo.AddComponent<RadialFirstPersonController>();
                controller.Configure(runtime, pivot, settings);
                player = playerGo.transform;
            }

            PlanetRuntimeRoot runtimeRoot = runtime.GetComponent<PlanetRuntimeRoot>();
            if (runtimeRoot != null) runtimeRoot.Configure(settings, player);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[Planet Prototype] Procedural planet, player input, camera, collision, and radial gravity are installed in PlanetDevelopment.");
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
