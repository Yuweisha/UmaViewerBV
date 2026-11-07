using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// A2U creates materials by name, so no scene/material reference retains these
// shaders automatically. Install actual imported objects before player builds.
public static class PreviewerA2UShaderRetention
{
    public static void Ensure()
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").First();
        var serialized = new SerializedObject(settings);
        var included = serialized.FindProperty("m_AlwaysIncludedShaders");
        foreach (string name in new[]
        {
            "Plane_NoTexAlpha_Normal", "Plane_NoTexAlpha_Add",
            "Plane_NoTexAlpha_StencilMask", "Plane_NoTexAlpha_StencilAlphaMask",
            "Text_Main_Normal", "Text_Main_Add",
            "Text_Main_Normal_Gradation", "Text_Main_Add_Gradation"
        })
        {
            string path = "Assets/Shaders/AnimateToUnity/" + name + ".shader";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || shader.name != "AnimateToUnity/" + name)
                throw new InvalidOperationException("A2U shader asset missing: " + path);
            bool found = false;
            for (int i = 0; i < included.arraySize; i++)
                if (included.GetArrayElementAtIndex(i).objectReferenceValue == shader) { found = true; break; }
            if (!found)
            {
                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
            }
            Debug.Log("[A2UShaderRetention] " + shader.name + " " + AssetDatabase.AssetPathToGUID(path));
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
}
