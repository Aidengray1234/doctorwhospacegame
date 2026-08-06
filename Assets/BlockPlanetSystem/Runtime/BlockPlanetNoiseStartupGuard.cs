using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoctorWho.BlockPlanets
{
    /// <summary>
    /// Temporary compatibility guard for the legacy BlockPlanetSystem.
    /// The replacement VoxelUniverse is built separately, but the old scene must
    /// remain stable while that replacement is under construction.
    /// </summary>
    internal static class BlockPlanetNoiseStartupGuard
    {
        private static readonly FieldInfo NoiseField = typeof(BlockPlanetWorld).GetField(
            "noise", BindingFlags.Instance | BindingFlags.NonPublic);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneCallback()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeFirstScene()
        {
            InitializeLoadedWorlds();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InitializeLoadedWorlds();
        }

        private static void InitializeLoadedWorlds()
        {
            if (NoiseField == null)
            {
                Debug.LogError("[Block Planet Guard] Could not locate BlockPlanetWorld.noise.");
                return;
            }

            BlockPlanetWorld[] worlds = Object.FindObjectsOfType<BlockPlanetWorld>(true);
            for (int i = 0; i < worlds.Length; i++)
            {
                BlockPlanetWorld world = worlds[i];
                if (world == null || world.Settings == null) continue;
                if (NoiseField.GetValue(world) != null) continue;

                NoiseField.SetValue(world, new BlockPlanetNoise(world.Settings.seed));
            }
        }
    }
}
