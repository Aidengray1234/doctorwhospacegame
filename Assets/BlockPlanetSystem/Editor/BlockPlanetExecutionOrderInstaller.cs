using DoctorWho.BlockPlanets;
using UnityEditor;
using UnityEngine;

namespace DoctorWho.BlockPlanets.Editor
{
    /// <summary>
    /// Ensures the legacy world initializes before player/controllers that query
    /// its surface during their first physics frame.
    /// </summary>
    [InitializeOnLoad]
    internal static class BlockPlanetExecutionOrderInstaller
    {
        private const int DesiredExecutionOrder = -10000;

        static BlockPlanetExecutionOrderInstaller()
        {
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Apply;
                return;
            }

            string[] guids = AssetDatabase.FindAssets("BlockPlanetWorld t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null || script.GetClass() != typeof(BlockPlanetWorld)) continue;

                if (MonoImporter.GetExecutionOrder(script) != DesiredExecutionOrder)
                    MonoImporter.SetExecutionOrder(script, DesiredExecutionOrder);
                return;
            }
        }
    }
}
