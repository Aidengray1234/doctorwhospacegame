using System;
using System.IO;
using DoctorWho.VoxelUniverse.Collision;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Interaction;
using DoctorWho.VoxelUniverse.Player;
using DoctorWho.VoxelUniverse.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class NoWarpInfiniteRenderingInstaller
    {
        private const string ScenePath = "Assets/PlanetSystem/Scenes/PlanetDevelopment.unity";
        private const string SettingsPath = "Assets/VoxelUniverse/Materials/VoxelUniverseSettings.asset";
        private const string OpaqueMaterialPath = "Assets/VoxelUniverse/Materials/VoxelOpaque.mat";
        private const string WaterMaterialPath = "Assets/VoxelUniverse/Materials/VoxelWater.mat";
        private const string LodMaterialPath = "Assets/VoxelUniverse/Materials/VoxelPlanetClipmap.mat";
        private const string AtlasPath = "Assets/BlockPlanetSystem/Textures/PlanetcraftBlockAtlas.png";

        [MenuItem("Tools/Voxel Universe/Install No-Warp Infinite Rendering")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation before installing the renderer.", "OK");
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

            GameObject root = FindRoot(scene, "Voxel Universe");
            if (root == null || IsUnderPlayground(root.transform))
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The Voxel Universe root is missing or is under Playground. Nothing was changed.", "OK");
                return;
            }

            VoxelUniverseWorld world = root.GetComponent<VoxelUniverseWorld>();
            VoxelUniverseSettings settings = AssetDatabase.LoadAssetAtPath<VoxelUniverseSettings>(SettingsPath);
            Material opaque = AssetDatabase.LoadAssetAtPath<Material>(OpaqueMaterialPath);
            Material water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            Shader lodShader = Shader.Find("DoctorWho/Voxel Planet Clipmap");
            if (world == null || settings == null || opaque == null || water == null || lodShader == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Required VoxelUniverse assets or the new clipmap shader are missing. Wait for compilation and run this menu again.", "OK");
                return;
            }

            Transform player = root.transform.Find("Voxel Player");
            if (player == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Voxel Player was not found beneath the Voxel Universe root.", "OK");
                return;
            }

            settings.ApplyNoWarpInfiniteRenderingDefaults();
            ConfigureAtlasForCubes();
            Material lodMaterial = LoadOrCreateLodMaterial(lodShader);

            TangentVoxelClipmap patch = GetOrAdd<TangentVoxelClipmap>(root);
            PlanetInfiniteLodRenderer lod = GetOrAdd<PlanetInfiniteLodRenderer>(root);
            NoWarpRuntimeValidator validator = GetOrAdd<NoWarpRuntimeValidator>(root);
            LogicalWorldUpdateSuppressor suppressor = GetOrAdd<LogicalWorldUpdateSuppressor>(root);
            suppressor.Configure(world);
            patch.Configure(world, player, opaque, water);
            lod.Configure(world, player, lodMaterial);

            FarPlanetRenderer oldFar = root.GetComponent<FarPlanetRenderer>();
            if (oldFar != null) oldFar.enabled = false;
            DisableChild(root.transform, "Complete Blocky Far Planet");
            DisableChild(root.transform, "Far Planet Hole Fallback");
            DisableChild(root.transform, "Complete Far Planet");
            DisableChild(root.transform, "Near Voxel Sections");

            // The logical world is enabled in the saved scene so Awake initializes its
            // deterministic generator. LogicalWorldUpdateSuppressor disables only its old
            // warped section-streaming Update loop before the first gameplay frame.
            world.enabled = true;

            VoxelCollisionWorld collision = root.GetComponent<VoxelCollisionWorld>();
            if (collision != null) collision.Configure(world);
            VoxelPlayerController controller = player.GetComponent<VoxelPlayerController>();
            VoxelInteractor interactor = player.GetComponent<VoxelInteractor>();
            if (controller == null || interactor == null)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Player controller or interactor is missing. Nothing was saved.", "OK");
                return;
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(patch);
            EditorUtility.SetDirty(lod);
            EditorUtility.SetDirty(validator);
            EditorUtility.SetDirty(suppressor);
            EditorUtility.SetDirty(lodMaterial);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;

            Debug.Log("[Voxel Universe] Installed true-cube tangent terrain and complete middle/far planet clipmaps. Logical spherical generation and saves were preserved. Playground was not modified.");
            EditorUtility.DisplayDialog("Voxel Universe",
                "No-warp rendering installed.\n\nNear terrain now uses true 1x1x1 cubes in a recentering tangent patch. Middle and far clipmaps cover the complete planet so unloaded terrain is never empty. The rejected warped near-section renderer is disabled.\n\nPlayground was not touched.",
                "OK");
        }

        private static Material LoadOrCreateLodMaterial(Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(LodMaterialPath);
            if (material == null)
            {
                EnsureFolder("Assets/VoxelUniverse/Materials");
                material = new Material(shader);
                material.name = "VoxelPlanetClipmap";
                AssetDatabase.CreateAsset(material, LodMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Ambient", 0.28f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureAtlasForCubes()
        {
            TextureImporter importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return null;
        }

        private static T GetOrAdd<T>(GameObject value) where T : Component
        {
            T component = value.GetComponent<T>();
            return component != null ? component : value.AddComponent<T>();
        }

        private static void DisableChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) child.gameObject.SetActive(false);
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
