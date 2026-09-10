using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupPlayerHUDFeedback
{
    const string PrefabPath = "Assets/Prefabs/FloatingText.prefab";

    [MenuItem("Tools/Setup Player HUD Feedback")]
    static void Setup()
    {
        var balanceTextObj = GameObject.Find("BalanceText");
        if (balanceTextObj == null)
        {
            Debug.LogError("SetupPlayerHUDFeedback: couldn't find 'BalanceText' - run Tools > Setup Player HUD first.");
            return;
        }

        var canvasTransform = balanceTextObj.transform.parent;

        // --- Build (or reuse) the FloatingText prefab asset ---
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabAsset == null)
        {
            var temp = new GameObject("FloatingText", typeof(RectTransform));
            var text = temp.AddComponent<TextMeshProUGUI>();
            text.text = "+$0";
            text.fontSize = 28;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = false;

            var rect = temp.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 50f);
            text.ForceMeshUpdate();

            temp.AddComponent<FloatingCombatText>();

            prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
        }

        // --- Feedback anchor, positioned just below the balance text ---
        var anchorObj = GameObject.Find("FeedbackAnchor");
        if (anchorObj == null)
        {
            anchorObj = new GameObject("FeedbackAnchor", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(anchorObj, "Create FeedbackAnchor");
            anchorObj.transform.SetParent(canvasTransform, false);

            var anchorRect = anchorObj.GetComponent<RectTransform>();
            var balanceRect = balanceTextObj.GetComponent<RectTransform>();
            anchorRect.anchorMin = balanceRect.anchorMin;
            anchorRect.anchorMax = balanceRect.anchorMax;
            anchorRect.pivot = balanceRect.pivot;
            anchorRect.anchoredPosition = balanceRect.anchoredPosition + new Vector2(0f, -60f);
        }

        if (anchorObj.GetComponent<PlayerHUDFeedback>() == null)
        {
            var feedback = anchorObj.AddComponent<PlayerHUDFeedback>();
            var so = new SerializedObject(feedback);
            so.FindProperty("feedbackPrefab").objectReferenceValue = prefabAsset.GetComponent<FloatingCombatText>();
            so.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = anchorObj;

        Debug.Log("PlayerHUD feedback set up: FloatingText prefab created, FeedbackAnchor placed below BalanceText, PlayerHUDFeedback wired. Save the scene to keep it.");
    }
}
