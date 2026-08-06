using UnityEditor;
using UnityEngine;

namespace DoctorWho.Planets.Editor
{
    [CustomEditor(typeof(PlanetPrototypeGenerator))]
    public sealed class PlanetPrototypeGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerate Planet", GUILayout.Height(32f)))
            {
                ((PlanetPrototypeGenerator)target).Regenerate();
                EditorUtility.SetDirty(target);
            }
        }
    }
}
