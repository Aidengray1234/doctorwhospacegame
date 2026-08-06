using System;
using DoctorWho.VoxelUniverse.Collision;
using DoctorWho.VoxelUniverse.Interaction;
using DoctorWho.VoxelUniverse.Player;
using DoctorWho.VoxelUniverse.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class StableGridRenderingInstaller
    {
        private const string RootName = "Stable Grid Rendering V2";

        [MenuItem("Tools/Voxel Universe/Install Stable Grid Rendering V2")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation before installing.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name.IndexOf("Playground",
                StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Installation was rejected because the active scene is invalid or contains Playground.", "OK");
                return;
            }

            VoxelUniverseWorld world = UnityEngine.Object.FindObjectOfType<VoxelUniverseWorld>();
            VoxelPlayerController player = UnityEngine.Object.FindObjectOfType<VoxelPlayerController>();
            if (world == null || player == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "A VoxelUniverseWorld and VoxelPlayerController are required in PlanetDevelopment.", "OK");
                return;
            }

            GameObject root = FindOrCreateChild(world.transform, RootName);
            StableGridEditStore editStore = GetOrAdd<StableGridEditStore>(root);
            StableCartesianVoxelGrid grid = GetOrAdd<StableCartesianVoxelGrid>(root);
            StablePlanetCoverRenderer cover = GetOrAdd<StablePlanetCoverRenderer>(root);
            StableVoxelRuntimeValidator validator = GetOrAdd<StableVoxelRuntimeValidator>(root);

            Material opaque = GetWorldMaterial(world, "opaqueMaterial");
            Material water = GetWorldMaterial(world, "waterMaterial");
            if (opaque == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                opaque = new Material(lit != null ? lit : Shader.Find("Standard"));
                opaque.name = "Stable Voxel Fallback";
            }
            if (water == null) water = opaque;
            ConfigureAtlasTexture(opaque);
            Material coverMaterial = GetOrCreateCoverMaterial();

            editStore.Configure(world);
            grid.Configure(world, player.transform, opaque, water, editStore);
            cover.Configure(world, player.transform, coverMaterial);
            validator.Configure(grid, cover, player.transform);

            VoxelCollisionWorld collision = UnityEngine.Object.FindObjectOfType<VoxelCollisionWorld>();
            if (collision != null) collision.Configure(world, grid);
            VoxelInteractor interactor = UnityEngine.Object.FindObjectOfType<VoxelInteractor>();
            if (interactor != null) interactor.ConfigureStable(grid, collision);

            DisableLegacyNearRenderers(world, root.transform);
            Camera camera = player.PlayerCamera != null ? player.PlayerCamera : Camera.main;
            if (camera != null)
            {
                camera.farClipPlane = Mathf.Max(camera.farClipPlane,
                    world.Settings != null ? world.Settings.groundRadius * 80f : 25000f);
                EditorUtility.SetDirty(camera);
            }

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(player);
            if (collision != null) EditorUtility.SetDirty(collision);
            if (interactor != null) EditorUtility.SetDirty(interactor);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Stable Voxel Grid] Installed V2. Fixed body-centered cube coordinates, "
                + "padded atlas UVs, complete middle/far cover, stable DDA, and logical collision. "
                + "Playground was not touched.");
            EditorUtility.DisplayDialog("Voxel Universe",
                "Stable Grid Rendering V2 is installed.\n\nClear the Console and enter Play Mode. "
                + "Wait for the F3 planet cover to show COMPLETE and for the validation PASS message.",
                "OK");
        }

        private static Material GetWorldMaterial(VoxelUniverseWorld world, string propertyName)
        {
            SerializedObject serialized = new SerializedObject(world);
            SerializedProperty property = serialized.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as Material : null;
        }

        private static Material GetOrCreateCoverMaterial()
        {
            const string directory = "Assets/VoxelUniverse/Materials";
            const string path = directory + "/StablePlanetCover.mat";
            if (!AssetDatabase.IsValidFolder(directory))
            {
                if (!AssetDatabase.IsValidFolder("Assets/VoxelUniverse"))
                    AssetDatabase.CreateFolder("Assets", "VoxelUniverse");
                AssetDatabase.CreateFolder("Assets/VoxelUniverse", "Materials");
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("DoctorWho/VoxelUniverse/Stable Planet Cover");
            if (material == null)
            {
                material = new Material(shader != null ? shader
                    : Shader.Find("Universal Render Pipeline/Unlit"));
                material.name = "Stable Planet Cover";
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null) material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureAtlasTexture(Material opaque)
        {
            if (opaque == null) return;
            Texture2D atlas = opaque.mainTexture as Texture2D;
            if (atlas == null) return;
            string path = AssetDatabase.GetAssetPath(atlas);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool changed = importer.filterMode != FilterMode.Point
                || importer.wrapMode != TextureWrapMode.Clamp || importer.mipmapEnabled;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (changed) importer.SaveAndReimport();
        }

        private static void DisableLegacyNearRenderers(VoxelUniverseWorld world, Transform stableRoot)
        {
            Transform oldSections = world.transform.Find("Near Voxel Sections");
            if (oldSections != null) oldSections.gameObject.SetActive(false);
            MonoBehaviour[] behaviours = world.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.transform.IsChildOf(stableRoot)) continue;
                string typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("TangentVoxel", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("LocalVoxelPatch", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("NoWarpPatch", StringComparison.OrdinalIgnoreCase) >= 0)
                    behaviour.enabled = false;
            }
            Transform[] transforms = world.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == stableRoot || current.IsChildOf(stableRoot)) continue;
                string name = current.name;
                if (name.IndexOf("Tangent Voxel", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Local Voxel Patch", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("No-Warp Near", StringComparison.OrdinalIgnoreCase) >= 0)
                    current.gameObject.SetActive(false);
            }
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null) return found.gameObject;
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Install Stable Grid Rendering V2");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component == null) component = Undo.AddComponent<T>(gameObject);
            return component;
        }
    }
}
