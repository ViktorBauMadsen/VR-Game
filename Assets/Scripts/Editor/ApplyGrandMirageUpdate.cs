using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Casino > Apply Grand Mirage Update (all)
//
// One click for everything in this update, run with the casino scene open:
// VR render settings, patrons + Bond, desert surroundings, the renamed
// marquee - then saves the scene. Each step also has its own menu item.
public static class ApplyGrandMirageUpdate
{
    [MenuItem("Tools/Casino/Apply Grand Mirage Update (all)")]
    static void ApplyAll()
    {
        ApplyVrPerformance.Apply();
        SetupPatrons.Build();
        DecorateInterior.BuildDesert();
        DecorateInterior.RenameCasino();
        UpgradeBar.Upgrade();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"ApplyGrandMirageUpdate: done, '{scene.name}' saved.");
    }
}
