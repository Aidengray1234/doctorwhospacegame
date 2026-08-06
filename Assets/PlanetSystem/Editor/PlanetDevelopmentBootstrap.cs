using System.IO;
using DoctorWho.Planets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.Planets.Editor
{
    [InitializeOnLoad]
    internal static class PlanetDevelopmentBootstrap
    {
        private const string RootFolder = "Assets/PlanetSystem";
        private const string SettingsFolder = RootFolder + "/Settings";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string SettingsPath = SettingsFolder + "/DefaultPlanetGenerationSettings.asset";
        private const string ScenePath = SceneFolder + "/PlanetDevelopment.unity";

        static PlanetDevelopmentBootstrap()
        {
            EditorApplication.delayCall += CreateFoundationIfMissing;
        }

        [MenuItem("Tools/Doctor Who/Create Planet Development Foundation")]
        private static void CreateFoundationIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            EnsureFolder(RootFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(SceneFolder);

            PlanetGenerationSettings settings = AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PlanetGenerationSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            if (!File.Exists(ScenePath))
            {
                Scene previousScene = SceneManager.GetActiveScene();
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                scene.name = "PlanetDevelopment";

                var systems = new GameObject("Planet Systems");
                SceneManager.MoveGameObjectToScene(systems, scene);

                var planet = new GameObject("Planet Runtime");
                planet.transform.SetParent(systems.transform, false);
                var streaming = planet.AddComponent<PlanetStreamingController>();
                var runtimeRoot = planet.AddComponent<PlanetRuntimeRoot>();

                var trackingTarget = new GameObject("Tracking Target");
                trackingTarget.transform.SetParent(systems.transform, false);
                trackingTarget.transform.position = new Vector3(0f, settings.radius + 10f, 0f);
                runtimeRoot.Configure(settings, trackingTarget.transform);

                var lightObject = new GameObject("Directional Light");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid())
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Planet Foundation] Runtime architecture and PlanetDevelopment scene are ready. Playground was not modified.");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
