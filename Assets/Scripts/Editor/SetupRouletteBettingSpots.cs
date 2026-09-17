using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Wires the BettingSpot component onto the 46 marker BoxColliders hand-
// placed as children of RouletteBetTable, and (as a separate, clearly
// named command) flips those same colliders to triggers. Kept idempotent
// (get-or-add, never destroy/rebuild) since these are unique hand-placed
// objects, unlike SetupRouletteWheel which owns geometry it's free to
// regenerate from scratch.
public static class SetupRouletteBettingSpots
{
    const string BetTableName = "RouletteBetTable";

    [MenuItem("Tools/Roulette/Add Betting Spot Components")]
    static void AddBettingSpotComponents()
    {
        var betTable = GameObject.Find(BetTableName);
        if (betTable == null)
        {
            Debug.LogError($"SetupRouletteBettingSpots: no '{BetTableName}' found in the scene.");
            return;
        }

        int configured = 0, skipped = 0;
        foreach (Transform child in betTable.transform)
        {
            if (!TryParseSpotName(child.name, out var type, out int number, out var kind))
            {
                Debug.LogWarning($"SetupRouletteBettingSpots: couldn't parse spot name '{child.name}' - skipped.");
                skipped++;
                continue;
            }

            var spot = child.GetComponent<BettingSpot>();
            if (spot == null) spot = Undo.AddComponent<BettingSpot>(child.gameObject);

            var so = new SerializedObject(spot);
            so.FindProperty("spotType").enumValueIndex = (int)type;
            so.FindProperty("number").intValue = number;
            so.FindProperty("evenMoneyKind").enumValueIndex = (int)kind;
            so.ApplyModifiedProperties();
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupRouletteBettingSpots: configured {configured} spot(s), skipped {skipped}.");
    }

    // Separate from the command above on purpose - this is a live behavior
    // change to colliders that were hand-placed one at a time, not just an
    // additive component, so it should show up as its own clean, reviewable
    // diff rather than being folded into the pass above.
    [MenuItem("Tools/Roulette/Make Betting Spot Colliders Triggers")]
    static void MakeCollidersTriggers()
    {
        var betTable = GameObject.Find(BetTableName);
        if (betTable == null)
        {
            Debug.LogError($"SetupRouletteBettingSpots: no '{BetTableName}' found in the scene.");
            return;
        }

        int changed = 0;
        foreach (Transform child in betTable.transform)
        {
            var collider = child.GetComponent<Collider>();
            if (collider == null || collider.isTrigger) continue;

            Undo.RecordObject(collider, "Make Betting Spot Collider Trigger");
            collider.isTrigger = true;
            changed++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"SetupRouletteBettingSpots: switched {changed} collider(s) to trigger - chips rest on CardTable's own collider instead of these markers.");
    }

    static bool TryParseSpotName(string name, out BettingSpot.SpotType type, out int number, out BettingSpot.EvenMoneyKind kind)
    {
        type = BettingSpot.SpotType.StraightUp;
        number = 0;
        kind = BettingSpot.EvenMoneyKind.Low;

        if (int.TryParse(name, out int straightNumber) && straightNumber >= 0 && straightNumber <= 36)
        {
            type = BettingSpot.SpotType.StraightUp;
            number = straightNumber;
            return true;
        }

        switch (name)
        {
            case "1st 12": type = BettingSpot.SpotType.Dozen; number = 1; return true;
            case "2nd 12": type = BettingSpot.SpotType.Dozen; number = 2; return true;
            case "3rd 12": type = BettingSpot.SpotType.Dozen; number = 3; return true;
            case "1to18": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.Low; return true;
            case "19to36": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.High; return true;
            case "Even": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.Even; return true;
            case "Odd": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.Odd; return true;
            case "Red": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.Red; return true;
            case "Black": type = BettingSpot.SpotType.EvenMoney; kind = BettingSpot.EvenMoneyKind.Black; return true;
            default: return false;
        }
    }
}
