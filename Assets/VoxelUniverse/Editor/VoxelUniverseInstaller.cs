using System;
using System.IO;
using DoctorWho.VoxelUniverse.Atmosphere;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Collision;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Interaction;
using DoctorWho.VoxelUniverse.Inventory;
using DoctorWho.VoxelUniverse.Player;
using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Saves;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class VoxelUniverseInstaller
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string AssetRoot = "Assets/VoxelUniverse";
        private const string SettingsPath = AssetRoot + "/Materials/VoxelUniverseSettings.asset";
        private const string BodyPath = AssetRoot + "/Materials/PrimaryWorld.asset";
        private const string OpaqueMaterialPath = AssetRoot + "/Materials/VoxelOpaque.mat";
        private const string WaterMaterialPath = AssetRoot + "/Materials/VoxelWater.mat";
        private const string FarMaterialPath = AssetRoot + "/Materials/VoxelFarPlanet.mat";
        private const string OutlineMaterialPath = AssetRoot + "/Materials/VoxelOutline.mat";
        private const string AtmosphereMaterialPath = AssetRoot + "/Materials/VoxelAtmosphere.mat";

        [MenuItem("Tools/Voxel Universe/Install Production Runtime")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Voxel Universe", "Exit Play Mode before installing.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            EnsureFolder(AssetRoot);
            EnsureFolder(AssetRoot + "/Materials");

            VoxelUniverseSettings settings = LoadOrCreateSettings();
            CelestialBodyDefinition body = LoadOrCreateBody(settings);
            Shader shader = Shader.Find("DoctorWho/Voxel Vertex Color");
            if (shader == null)
            {
                EditorUtility.DisplayDialog(
                    "Voxel Universe",
                    "The Voxel Vertex Color shader has not compiled yet. Wait for Unity to finish importing, then run the installer again.",
                    "OK");
                return;
            }

            Material opaque = LoadOrCreateMaterial(OpaqueMaterialPath, shader, Color.white, false, true, CullMode.Back);
            Material water = LoadOrCreateMaterial(WaterMaterialPath, shader, new Color(1f, 1f, 1f, 0.72f), true, false, CullMode.Back);
            Material far = LoadOrCreateMaterial(FarMaterialPath, shader, new Color(0.18f, 0.42f, 0.22f, 1f), false, true, CullMode.Back);
            Material outline = LoadOrCreateMaterial(OutlineMaterialPath, shader, new Color(1f, 0.86f, 0.18f, 1f), true, false, CullMode.Off);
            Material atmosphere = LoadOrCreateMaterial(AtmosphereMaterialPath, shader, new Color(0.28f, 0.56f, 1f, 0.11f), true, false, CullMode.Front);

            GameObject root = FindOrCreateRoot(scene, "Voxel Universe");
            root.transform.position = Vector3.zero;

            VoxelSaveSystem saves = GetOrAdd<VoxelSaveSystem>(root);
            saves.Configure(settings.saveVersion, settings.generatorVersion);

            GameObject playerObject = FindOrCreateChild(root.transform, "Voxel Player");
            VoxelInventory inventory = GetOrAdd<VoxelInventory>(playerObject);
            VoxelPlayerController player = GetOrAdd<VoxelPlayerController>(playerObject);
            VoxelInteractor interactor = GetOrAdd<VoxelInteractor>(playerObject);

            GameObject pivotObject = FindOrCreateChild(playerObject.transform, "Camera Pivot");
            pivotObject.transform.localPosition = new Vector3(0f, settings.capsuleHeight - 0.15f, 0f);
            GameObject cameraObject = FindOrCreateChild(pivotObject.transform, "Player Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.localPosition = Vector3.zero;
            Camera camera = GetOrAdd<Camera>(cameraObject);
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200000f;
            camera.fieldOfView = 75f;
            GetOrAdd<AudioListener>(cameraObject);

            VoxelUniverseWorld world = GetOrAdd<VoxelUniverseWorld>(root);
            VoxelCollisionWorld collision = GetOrAdd<VoxelCollisionWorld>(root);
            FarPlanetRenderer farRenderer = GetOrAdd<FarPlanetRenderer>(root);
            VoxelUniverseDiagnostics diagnostics = GetOrAdd<VoxelUniverseDiagnostics>(root);
            CelestialLightingController celestialLighting = GetOrAdd<CelestialLightingController>(root);
            AtmosphereController atmosphereController = GetOrAdd<AtmosphereController>(root);

            Light primarySun = CreateDirectionalLight(root.transform, "Primary Sun", new Color(1f, 0.94f, 0.84f), 1.15f, true);
            Light secondarySun = CreateDirectionalLight(root.transform, "Secondary Sun", new Color(0.58f, 0.72f, 1f), 0.28f, false);

            world.Configure(settings, body, playerObject.transform, opaque, water, saves);
            collision.Configure(world);
            inventory.Configure(saves, true);
            player.Configure(world, collision, inventory, pivotObject.transform, camera);
            interactor.Configure(world, player, inventory, camera, outline);
            farRenderer.Configure(world, far);
            diagnostics.Configure(world, saves);
            celestialLighting.Configure(world, primarySun, secondarySun);
            atmosphereController.Configure(world, playerObject.transform, atmosphere);

            VoxelAddress spawn = world.FindSurfaceAddress(Vector3.up);
            Vector3 spawnCenter = world.GetBlockCenter(spawn);
            Vector3 spawnUp = (spawnCenter - world.Center).normalized;
            playerObject.transform.position = spawnCenter + spawnUp * (settings.capsuleHeight + 1.2f);
            playerObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, spawnUp);

            DisableRejectedLegacyRuntime(root);
            DisableExtraAudioListeners(camera);
            DisableExtraMainCameras(camera);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(body);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;

            Debug.Log("[Voxel Universe] Installed the new runtime root, logical voxel player, threaded section world, DDA interaction, inventory/save data, far fallback planet, atmosphere and two-sun lighting. Playground was not modified.");
            EditorUtility.DisplayDialog(
                "Voxel Universe",
                "Installed Voxel Universe into PlanetDevelopment.\n\nPress Play to compile/test. The rejected planet runtimes were disabled, not deleted. Playground was not touched.",
                "OK");
        }

        private static VoxelUniverseSettings LoadOrCreateSettings()
        {
            VoxelUniverseSettings settings = AssetDatabase.LoadAssetAtPath<VoxelUniverseSettings>(SettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<VoxelUniverseSettings>();
            settings.ClampValues();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static CelestialBodyDefinition LoadOrCreateBody(VoxelUniverseSettings settings)
        {
            CelestialBodyDefinition body = AssetDatabase.LoadAssetAtPath<CelestialBodyDefinition>(BodyPath);
            if (body != null) return body;
            body = ScriptableObject.CreateInstance<CelestialBodyDefinition>();
            body.stableKey = settings.stableBodyKey;
            body.radius = settings.groundRadius;
            body.gravityParameter = settings.gravity;
            body.seed = settings.seed;
            body.seaLevel = settings.seaLevel;
            AssetDatabase.CreateAsset(body, BodyPath);
            return body;
        }

        private static Material LoadOrCreateMaterial(
            string path,
            Shader shader,
            Color color,
            bool transparent,
            bool zWrite,
            CullMode cull)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", zWrite ? 1f : 0f);
            material.SetFloat("_Cull", (float)cull);
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return new GameObject(name);
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found.gameObject;
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static Light CreateDirectionalLight(
            Transform parent,
            string name,
            Color color,
            float intensity,
            bool shadows)
        {
            GameObject lightObject = FindOrCreateChild(parent, name);
            Light light = GetOrAdd<Light>(lightObject);
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }

        private static void DisableRejectedLegacyRuntime(GameObject replacementRoot)
        {
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            string[] rejectedTypes =
            {
                "DoctorWho.BlockPlanets.BlockPlanetWorld",
                "DoctorWho.BlockPlanets.BlockPlanetPlayerController",
                "DoctorWho.Planets.PlanetPrototypeGenerator",
                "DoctorWho.Planets.PlanetStreamingController",
                "DoctorWho.Planets.RadialFirstPersonController"
            };

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.transform.IsChildOf(replacementRoot.transform)) continue;
                if (IsUnderPlayground(behaviour.transform)) continue;
                string fullName = behaviour.GetType().FullName;
                bool rejected = false;
                for (int typeIndex = 0; typeIndex < rejectedTypes.Length; typeIndex++)
                    if (fullName == rejectedTypes[typeIndex]) { rejected = true; break; }
                if (!rejected) continue;

                behaviour.enabled = false;
                Renderer[] renderers = behaviour.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    renderers[rendererIndex].enabled = false;
            }
        }

        private static bool IsUnderPlayground(Transform value)
        {
            Transform current = value;
            while (current != null)
            {
                if (current.name.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static void DisableExtraAudioListeners(Camera selectedCamera)
        {
            AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = listeners[i].gameObject == selectedCamera.gameObject;
        }

        private static void DisableExtraMainCameras(Camera selectedCamera)
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == selectedCamera) continue;
                if (IsUnderPlayground(cameras[i].transform)) continue;
                cameras[i].enabled = false;
            }
            selectedCamera.enabled = true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string name = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
