using System.IO;
using DoctorWho.BlockPlanets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.BlockPlanets.Editor
{
    internal static class BlockPlanetInstaller
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string SettingsFolder = "Assets/BlockPlanetSystem/Settings";
        private const string SettingsPath = SettingsFolder + "/DefaultBlockPlanetSettings.asset";
        private const string MaterialFolder = "Assets/BlockPlanetSystem/Materials";
        private const string AtlasPath = "Assets/BlockPlanetSystem/Textures/PlanetcraftBlockAtlas.png";
        private const string BlockMaterialPath = MaterialFolder + "/BlockPlanetAtlas.mat";
        private const string WaterMaterialPath = MaterialFolder + "/BlockPlanetWater.mat";
        private const string FarMaterialPath = MaterialFolder + "/BlockPlanetFar.mat";

        [MenuItem("Tools/Doctor Who/Block Planet/Install Or Repair V2")]
        private static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("[Block Planet V2] Stop Play Mode and wait for compilation first.");
                return;
            }
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[Block Planet V2] PlanetDevelopment scene was not found.");
                return;
            }

            EnsureFolder(SettingsFolder);
            EnsureFolder(MaterialFolder);
            ConfigureAtlasImporter();
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null)
            {
                Debug.LogError("[Block Planet V2] Planetcraft texture atlas was not imported yet.");
                return;
            }

            BlockPlanetSettings settings = AssetDatabase.LoadAssetAtPath<BlockPlanetSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<BlockPlanetSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.radius = Mathf.Max(settings.radius, 128f);
            settings.faceResolution = Mathf.Max(settings.faceResolution, 256);
            settings.horizontalChunkRadius = Mathf.Max(settings.horizontalChunkRadius, 2);
            settings.chunkBuildsPerFrame = Mathf.Max(settings.chunkBuildsPerFrame, 6);
            settings.initialChunkBuildsPerFrame = Mathf.Max(settings.initialChunkBuildsPerFrame, 18);

            Shader opaqueShader = Shader.Find("DoctorWho/BlockPlanetAtlas");
            Shader waterShader = Shader.Find("DoctorWho/BlockPlanetWater");
            if (opaqueShader == null || waterShader == null)
            {
                Debug.LogError("[Block Planet V2] Shaders are not compiled yet. Wait, then run the installer again.");
                return;
            }
            Material blockMaterial = GetOrCreateMaterial(BlockMaterialPath, opaqueShader);
            blockMaterial.SetTexture("_BaseMap", atlas);
            blockMaterial.enableInstancing = true;
            Material waterMaterial = GetOrCreateMaterial(WaterMaterialPath, waterShader);
            waterMaterial.SetTexture("_BaseMap", atlas);
            Material farMaterial = GetOrCreateMaterial(FarMaterialPath, Shader.Find("Universal Render Pipeline/Lit") ?? opaqueShader);
            if (farMaterial.HasProperty("_BaseColor")) farMaterial.SetColor("_BaseColor", new Color(0.10f, 0.30f, 0.13f, 1f));
            else farMaterial.color = new Color(0.10f, 0.30f, 0.13f, 1f);
            if (farMaterial.HasProperty("_Smoothness")) farMaterial.SetFloat("_Smoothness", 0.02f);

            Scene current = SceneManager.GetActiveScene();
            bool alreadyOpen = current.path == ScenePath;
            Scene scene = alreadyOpen ? current : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "Planet Systems") root.SetActive(false);

            GameObject existing = FindRoot(scene, "Block Planet Systems");
            if (existing != null) Object.DestroyImmediate(existing);
            GameObject systems = new GameObject("Block Planet Systems");
            SceneManager.MoveGameObjectToScene(systems, scene);

            GameObject worldObject = new GameObject("Block Planet World");
            worldObject.transform.SetParent(systems.transform, false);
            GameObject player = new GameObject("Block Planet Player");
            player.transform.SetParent(systems.transform, false);

            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.36f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            Rigidbody body = player.AddComponent<Rigidbody>();
            body.mass = 78f;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Transform pivot = new GameObject("Camera Pivot").transform;
            pivot.SetParent(player.transform, false);
            pivot.localPosition = new Vector3(0f, 1.65f, 0f);
            GameObject cameraObject = new GameObject("Player Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pivot, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.nearClipPlane = settings.cameraNearClip;
            camera.fieldOfView = settings.cameraFieldOfView;
            camera.farClipPlane = Mathf.Max(6000f, settings.radius * 35f);

            BlockPlanetWorld world = worldObject.AddComponent<BlockPlanetWorld>();
            world.Configure(settings, blockMaterial, waterMaterial, farMaterial, player.transform);
            BlockInventory inventory = player.AddComponent<BlockInventory>();
            inventory.Configure(atlas, settings, world);
            BlockPlanetPlayerController controller = player.AddComponent<BlockPlanetPlayerController>();
            controller.Configure(world, pivot, settings, inventory);

            Vector3 direction = new Vector3(0.27f, 0.93f, 0.24f).normalized;
            player.transform.position = world.GetSurfacePoint(direction) + direction * 4f;
            player.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);

            if (Object.FindObjectOfType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Block Planet Sun");
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[Block Planet V2] Installed Planetcraft textures, prioritized chunk loading, core safety, radial player, mining/placing and inventory. Press Play.");
        }

        [MenuItem("Tools/Doctor Who/Block Planet/Frame Planet V2")]
        private static void FramePlanet()
        {
            BlockPlanetWorld world = Object.FindObjectOfType<BlockPlanetWorld>();
            if (world == null) return;
            Selection.activeGameObject = world.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.size = world.Settings.radius * 1.8f;
                SceneView.lastActiveSceneView.Repaint();
            }
        }

        private static void ConfigureAtlasImporter()
        {
            TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root;
            return null;
        }

        private static Material GetOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
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
