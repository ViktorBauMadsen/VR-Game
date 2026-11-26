using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Bar > Assign Bartender Voice Lines: fills the bartender's serve and
// "can't afford" lines with their text and the recorded clips in
// Assets/Audio/VoiceLines/Bartender.
public static class AssignBartenderVoice
{
    const string Folder = "Assets/Audio/VoiceLines/Bartender/";

    static readonly (string text, string file)[] Serve =
    {
        ("Coming right up.", "Coming right up.mp3"),
        ("Here you go. Enjoy!", "Here you go, enjoy!.mp3"),
        ("Excellent choice.", "Excellent choice!.mp3"),
        ("One of my favourites. Enjoy.", "One of my favourites.mp3"),
        ("There you are, friend.", "There you are, friend.mp3"),
    };

    static readonly (string text, string file)[] CantAfford =
    {
        ("Ah, you're a little short, friend.", "You're a little short.mp3"),
        ("Afraid that's a bit out of your budget tonight. Perhaps you should try your luck at the tables.", "Afraid that's a bit out of your budget.mp3"),
        ("Come back when the tables have been kind to you.", "Come back when the tables have been kind to you.mp3"),
    };

    static readonly (string text, string file)[] Greetings =
    {
        ("Evening! What can I get you?", "Evening! What can I get you.mp3"),
        ("Welcome back. The usual?", "Welcome back. The usual.mp3"),
        ("Take a seat, friend. What'll it be?", "Take a seat, friend. What'll it be.mp3"),
    };

    static readonly (string text, string file)[] SmallTalk =
    {
        ("Cigars are $100. Cocktails are on the menu.", "Cigars are $100. Cocktails are on the menu..mp3"),
        ("Press DRINKS on the bar and pick your poison.", "Press DRINKS on the bar and pick your poison..mp3"),
        ("Hold it up to your mouth - you'll figure it out.", "Hold it up to your mouth - you'll figure it out..mp3"),
        ("The roulette table's been hot tonight.", "The roulette table's been hot tonight..mp3"),
        ("Easy on the whiskey. It sneaks up on you.", "Easy on the whiskey. It sneaks up on you..mp3"),
        ("I've seen fortunes won and lost at those slots.", "I've seen fortunes won and lost at those slots..mp3"),
    };

    static readonly (string text, string file)[] BondReplies =
    {
        ("Shaken, not stirred. Right away, Mr. Bond.", "Shaken not stirred, right away mr bond.mp3"),
        ("Of course, Mr. Bond. The usual.", "Ofc mr bond, the usual.mp3"),
        ("Coming right up, sir.", "Coming right up sir.mp3"),
    };

    [MenuItem("Tools/Bar/Assign Bartender Voice Lines")]
    static void Assign()
    {
        var bartenders = Object.FindObjectsByType<Bartender>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (bartenders.Length == 0)
        {
            Debug.LogWarning("No Bartender in the open scene.");
            return;
        }

        foreach (var bartender in bartenders)
        {
            var so = new SerializedObject(bartender);
            Fill(so.FindProperty("greetings"), Greetings);
            Fill(so.FindProperty("smallTalk"), SmallTalk);
            Fill(so.FindProperty("serveLines"), Serve);
            Fill(so.FindProperty("cantAffordLines"), CantAfford);
            Fill(so.FindProperty("bondReplies"), BondReplies);
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(bartender.gameObject.scene);
        }
        Debug.Log($"Assigned bartender voice lines on {bartenders.Length} bartender(s). Save the scene to keep them.");
    }

    static void Fill(SerializedProperty array, (string text, string file)[] lines)
    {
        array.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + lines[i].file);
            if (clip == null) Debug.LogWarning("Missing voice clip: " + Folder + lines[i].file);
            element.FindPropertyRelative("text").stringValue = lines[i].text;
            element.FindPropertyRelative("clip").objectReferenceValue = clip;
        }
    }
}
