using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupPlayerHUD
{
    [MenuItem("Tools/Setup Player HUD")]
    static void Setup()
    {
        var mainCamera = GameObject.Find("Main Camera");
        if (mainCamera == null)
        {
            Debug.LogError("SetupPlayerHUD: couldn't find a GameObject named 'Main Camera' in the open scene.");
            return;
        }

        // --- PlayerBalance manager ---
        var balanceManager = GameObject.Find("PlayerBalance");
        if (balanceManager == null)
        {
            balanceManager = new GameObject("PlayerBalance");
            Undo.RegisterCreatedObjectUndo(balanceManager, "Create PlayerBalance");
        }
        if (balanceManager.GetComponent<PlayerBalance>() == null)
            Undo.AddComponent<PlayerBalance>(balanceManager);

        // --- Head-locked Canvas, parented to the camera ---
        var canvasObj = new GameObject("PlayerHUD", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasObj, "Create PlayerHUD Canvas");
        canvasObj.transform.SetParent(mainCamera.transform, false);

        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();

        var canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800, 450);
        canvasRect.localPosition = new Vector3(0f, 0f, 1f);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.001f;

        // --- Balance text, anchored to the top-left of the canvas ---
        var textObj = new GameObject("BalanceText", typeof(RectTransform));
        textObj.transform.SetParent(canvasObj.transform, false);

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "$1,000";
        text.color = Color.white;
        text.fontSize = 36;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableAutoSizing = false;

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(20f, -20f);
        textRect.sizeDelta = new Vector2(300f, 60f);
        text.ForceMeshUpdate();

        textObj.AddComponent<PlayerHUDBalanceDisplay>();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = canvasObj;

        Debug.Log("PlayerHUD set up: PlayerBalance manager created, Canvas parented to Main Camera with BalanceText wired to PlayerHUDBalanceDisplay. Save the scene to keep it.");
    }
}
