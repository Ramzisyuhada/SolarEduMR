using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(OrbitGenerator))]
public class OrbitGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (OrbitGenerator)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generator Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate Orbits"))
        {
            gen.GenerateOrbits();
        }

        if (GUILayout.Button("Generate Planets"))
        {
            gen.GeneratePlanets();
        }

        if (GUILayout.Button("Generate ALL (Orbits + Planets)"))
        {
            gen.GenerateAll();
        }

        if (GUILayout.Button("Auto-Assign to SolarGameManager"))
        {
            gen.AutoAssignToManager();
        }
    }
}
