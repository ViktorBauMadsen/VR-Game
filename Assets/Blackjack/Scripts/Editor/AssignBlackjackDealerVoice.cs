using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Blackjack > Assign Dealer Voice Lines: fills every blackjack
// dealer's lines with their text and the recorded clips in
// Assets/Audio/VoiceLines/BlackJack Dealer.
public static class AssignBlackjackDealerVoice
{
    const string Folder = "Assets/Audio/VoiceLines/BlackJack Dealer/";

    static readonly (string text, string file)[] Greetings =
    {
        ("Blackjack! Care for a hand?", "Blackjack! Care for a hand.mp3"),
        ("Take a seat - the cards are warm tonight.", "Take a seat - the cards are warm tonight..mp3"),
    };

    static readonly string[] Rules =
    {
        "Get closer to 21 than me without going over.",
        "Face cards count 10. Aces count 1 or 11.",
        "HIT takes a card. STAND keeps what you have.",
        "DOUBLE doubles your bet for exactly one more card.",
        "Blackjack pays 3 to 2. I stand on 17.",
    };

    static readonly string[] Wins = { "You win! Nicely played.", "Winner! Well done." };
    static readonly string[] Losses = { "House wins. Better luck next hand.", "Not this time." };

    static readonly (string field, string text)[] Singles =
    {
        ("blackjack", "Blackjack! Pays three to two."),
        ("push", "It's a push. Your bet comes back."),
        ("tooShort", "You're a little short for that bet."),
        ("notInHand", "You're not in this hand - press DEAL on the next one."),
        ("doubleFirstTwoOnly", "You can only double on your first two cards."),
        ("cantCoverDouble", "You can't cover the double - your bet stays as it is."),
    };

    [MenuItem("Tools/Blackjack/Assign Dealer Voice Lines")]
    static void Assign()
    {
        var dealers = Object.FindObjectsByType<BlackjackDealer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (dealers.Length == 0)
        {
            Debug.LogWarning("No BlackjackDealer in the open scene.");
            return;
        }

        foreach (var dealer in dealers)
        {
            var so = new SerializedObject(dealer);
            var greetings = so.FindProperty("greetings");
            greetings.arraySize = Greetings.Length;
            for (int i = 0; i < Greetings.Length; i++)
                Set(greetings.GetArrayElementAtIndex(i), Greetings[i].text, Greetings[i].file);

            FillList(so.FindProperty("rules"), Rules);
            FillList(so.FindProperty("wins"), Wins);
            FillList(so.FindProperty("losses"), Losses);

            foreach (var (field, text) in Singles)
                Set(so.FindProperty(field), text, text + ".mp3");

            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(dealer.gameObject.scene);
        }
        Debug.Log($"Assigned voice lines on {dealers.Length} blackjack dealer(s). Save the scene to keep them.");
    }

    // Each clip is named after its line.
    static void FillList(SerializedProperty array, string[] lines)
    {
        array.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++)
            Set(array.GetArrayElementAtIndex(i), lines[i], lines[i] + ".mp3");
    }

    static void Set(SerializedProperty line, string text, string file)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + file);
        if (clip == null) Debug.LogWarning("Missing voice clip: " + Folder + file);
        line.FindPropertyRelative("text").stringValue = text;
        line.FindPropertyRelative("clip").objectReferenceValue = clip;
    }
}
