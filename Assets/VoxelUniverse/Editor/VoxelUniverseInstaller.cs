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
        private const string AtlasPath = "Assets/BlockPlanetSystem/Textures/PlanetcraftBlockAtlas.png";
        private const string OpaqueMaterialPath = AssetRoot + "/Materials/VoxelOpaque.mat";
        private const string WaterMaterialPath = AssetRoot + "/Materials/VoxelWater.mat";
        private const string FarMaterialPath = AssetRoot + "/Materials/VoxelFarPlanet.mat";
        private const string OutlineMaterialPath = AssetRoot + "/Materials/VoxelOutline.mat";
        private const string AtmosphereMaterialPath = AssetRoot + "/Materials/VoxelAtmosphere.mat";

        [MenuItem("Tools/Voxel Universe/Install Or Repair Blocks, Textures and LOD")]
        public static void InstallVisualRepair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation before repairing the runtime.", "OK");
                return;
            }
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "PlanetDevelopment scene was not found at " + ScenePath, "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            EnsureFolder(AssetRoot);
            EnsureFolder(AssetRoot + "/Materials");
            ConfigureAtlasImporter();
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The Planetcraft block atlas is missing. Expected: " + AtlasPath, "OK");
                return;
            }

            Shader atlasShader = Shader.Find("DoctorWho/Voxel Universe Atlas");
            Shader atmosphereShader = Shader.Find("DoctorWho/Voxel Universe Atmosphere");
            if (atlasShader == null || atmosphereShader == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The replacement shaders have not compiled yet. Wait for Unity to finish importing and run this menu again.",
                    "OK");
                return;
            }

            VoxelUniverseSettings settings = LoadOrCreateSettings();
            settings.ApplyRecommendedVisualRepairDefaults();
            CelestialBodyDefinition body = LoadOrCreateBody(settings);
            ConfigureBody(body, settings);

            Material opaque = LoadOrCreateAtlasMaterial(OpaqueMaterialPath, atlasShader, atlas,
                Color.white, true, false, true, CullMode.Back);
            Material water = LoadOrCreateAtlasMaterial(WaterMaterialPath, atlasShader, atlas,
                new Color(1f, 1f, 1f, 0.76f), true, true, false, CullMode.Back);
            Material far = LoadOrCreateAtlasMaterial(FarMaterialPath, atlasShader, atlas,
                Color.white, false, false, true, CullMode.Back);
            Material outline = LoadOrCreateAtlasMaterial(OutlineMaterialPath, atlasShader, atlas,
                new Color(1f, 0.78f, 0.04f, 1f), false, true, false, CullMode.Off);
            Material atmosphere = LoadOrCreateAtmosphereMaterial(atmosphereShader, body);

            DisableLegacyRoots(scene);
            GameObject root = FindOrCreateRoot(scene, "Voxel Universe");
            root.transform.position = Vector3.zero;
            DestroyNamedChild(root.transform, "Complete Far Planet");
            DestroyNamedChild(root.transform, "Atmosphere Shell");

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
            camera.nearClipPlane = 0.025f;
            camera.farClipPlane = Mathf.Max(200000f, settings.groundRadius * 100f);
            camera.fieldOfView = 75f;
            GetOrAdd<AudioListener>(cameraObject);

            VoxelUniverseWorld world = GetOrAdd<VoxelUniverseWorld>(root);
            VoxelCollisionWorld collision = GetOrAdd<VoxelCollisionWorld>(root);
            FarPlanetRenderer farRenderer = GetOrAdd<FarPlanetRenderer>(root);
            VoxelUniverseDiagnostics diagnostics = GetOrAdd<VoxelUniverseDiagnostics>(root);
            CelestialLightingController celestialLighting = GetOrAdd<CelestialLightingController>(root);
            AtmosphereController atmosphereController = GetOrAdd<AtmosphereController>(root);

            Light primarySun = CreateDirectionalLight(root.transform, "Primary Sun",
                new Color(1f, 0.94f, 0.84f), 1.18f, true);
            Light secondarySun = CreateDirectionalLight(root.transform, "Secondary Sun",
                new Color(0.58f, 0.72f, 1f), 0.24f, false);
            RenderSettings.sun = primarySun;

            world.Configure(settings, body, playerObject.transform, opaque, water, saves);
            world.ClearGeneratedRenderers();
            collision.Configure(world);
            inventory.Configure(saves, true, atlas);
            player.Configure(world, collision, inventory, pivotObject.transform, camera);
            interactor.Configure(world, player, inventory, camera, outline);
            farRenderer.Configure(world, far);
            diagnostics.Configure(world, saves);
            celestialLighting.Configure(world, primarySun, secondarySun);
            atmosphereController.Configure(world, playerObject.transform, atmosphere);

            VoxelAddress spawn = world.FindSurfaceAddress(Vector3.up);
            Vector3 spawnCenter = world.GetBlockCenter(spawn);
            Vector3 spawnUp = (spawnCenter - world.Center).normalized;
            playerObject.transform.position = spawnCenter
                                              + spawnUp * (settings.capsuleHeight + 1.2f);
            playerObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, spawnUp);

            DisableRejectedLegacyRuntime(root);
            DisableExtraAudioListeners(camera);
            DisableExtraMainCameras(camera);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(opaque);
            EditorUtility.SetDirty(water);
            EditorUtility.SetDirty(far);
            EditorUtility.SetDirty(outline);
            EditorUtility.SetDirty(atmosphere);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;

            Debug.Log("[Voxel Universe Repair] Installed cuboid near blocks, Planetcraft atlas textures, a complete blocky far planet, single repaired inventory UI, larger 256-radius world, altitude-aware streaming, and layered atmosphere. Playground was not modified.");
            EditorUtility.DisplayDialog("Voxel Universe",
                "Visual and streaming repair installed.\n\nThe planet radius is now at least 256. Near terrain uses actual cuboid block faces and atlas textures. A complete low-cost blocky planet stays visible at distance. The old duplicate inventory and rejected planet runtime were disabled.\n\nPlayground was not touched.",
                "OK");
        }

        [MenuItem("Tools/Voxel Universe/Install Production Runtime")]
        public static void InstallProductionRuntime()
        {
            InstallVisualRepair();
        }

        private static VoxelUniverseSettings LoadOrCreateSettings()
        {
            VoxelUniverseSettings settings = AssetDatabase.LoadAssetAtPath<VoxelUniverseSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<VoxelUniverseSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            return settings;
        }

        private static CelestialBodyDefinition LoadOrCreateBody(VoxelUniverseSettings settings)
        {
            CelestialBodyDefinition body = AssetDatabase.LoadAssetAtPath<CelestialBodyDefinition>(BodyPath);
            if (body == null)
            {
                body = ScriptableObject.CreateInstance<CelestialBodyDefinition>();
                AssetDatabase.CreateAsset(body, BodyPath);
            }
            return body;
        }

        private static void ConfigureBody(CelestialBodyDefinition body, VoxelUniverseSettings settings)
        {
            body.stableKey = settings.stableBodyKey;
            body.displayName = "Primary Voxel World";
            body.bodyType = CelestialBodyType.RockyPlanet;
            body.radius = settings.groundRadius;
            body.gravityParameter = settings.gravity;
            body.seed = settings.seed;
            body.seaLevel = settings.seaLevel;
            body.hasOcean = true;
            body.hasAtmosphere = true;
            body.atmosphereHeight = Mathf.Max(body.atmosphereHeight, 72f);
            body.densityFalloff = Mathf.Clamp(body.densityFalloff, 0.12f, 0.32f);
            body.sphereOfInfluence = Math.Max(body.sphereOfInfluence, settings.groundRadius * 140d);
            body.voxelWorldEnabled = true;
        }

        private static Material LoadOrCreateAtlasMaterial(string path, Shader shader,
            Texture2D atlas, Color color, bool useTexture, bool transparent, bool zWrite, CullMode cull)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                material.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_UseTexture", useTexture ? 1f : 0f);
            material.SetFloat("_Ambient", transparent ? 0.30f : 0.23f);
            material.SetFloat("_SrcBlend", transparent
                ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat("_DstBlend", transparent
                ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", zWrite ? 1f : 0f);
            material.SetFloat("_Cull", (float)cull);
            material.renderQueue = transparent
                ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateAtmosphereMaterial(Shader shader,
            CelestialBodyDefinition body)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AtmosphereMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                material.name = "VoxelAtmosphere";
                AssetDatabase.CreateAsset(material, AtmosphereMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_AtmosphereColor", body.atmosphereColor);
            material.SetColor("_SunsetColor", body.sunsetColor);
            material.SetFloat("_Density", Mathf.Max(0.1f, body.densityFalloff * 4f));
            material.renderQueue = (int)RenderQueue.Transparent + 40;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureAtlasImporter()
        {
            TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        private static void DisableLegacyRoots(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null || IsUnderPlayground(root.transform)) continue;
                if (root.name == "Planet Systems" || root.name == "Block Planet Systems")
                    root.SetActive(false);
            }
        }

        private static void DisableRejectedLegacyRuntime(GameObject replacementRoot)
        {
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.transform.IsChildOf(replacementRoot.transform)) continue;
                if (IsUnderPlayground(behaviour.transform)) continue;
                string fullName = behaviour.GetType().FullName;
                if (string.IsNullOrEmpty(fullName)) continue;
                bool rejected = fullName.StartsWith("DoctorWho.BlockPlanets.", StringComparison.Ordinal)
                                || fullName == "DoctorWho.Planets.PlanetPrototypeGenerator"
                                || fullName == "DoctorWho.Planets.PlanetStreamingController"
                                || fullName == "DoctorWho.Planets.RadialFirstPersonController";
                if (!rejected) continue;
                behaviour.enabled = false;
                Renderer[] renderers = behaviour.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++) renderers[r].enabled = false;
            }
        }

        private static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            GameObject root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found.gameObject;
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void DestroyNamedChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) UnityEngine.Object.DestroyImmediate(found.gameObject);
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static Light CreateDirectionalLight(Transform parent, string name,
            Color color, float intensity, bool shadows)
        {
            GameObject lightObject = FindOrCreateChild(parent, name);
            Light light = GetOrAdd<Light>(lightObject);
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (name == "Primary Sun")
                lightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            else lightObject.transform.rotation = Quaternion.Euler(-18f, 128f, 0f);
            return light;
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
            {
                if (IsUnderPlayground(listeners[i].transform)) continue;
                listeners[i].enabled = listeners[i].gameObject == selectedCamera.gameObject;
            }
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
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
