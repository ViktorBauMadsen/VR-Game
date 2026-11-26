using UnityEngine;

// The recorded voice for a guest NPC: each of their lines (exactly as the
// NPC's script says it) with its clip. Guests are spawned at runtime, so they
// load this from Resources/Voices/<name> instead of holding clips themselves.
// A line with no entry here is shown in the bubble without a voice.
[CreateAssetMenu(menuName = "Casino/NPC Voice")]
public sealed class NpcVoice : ScriptableObject
{
    public VoiceLine[] lines;

    [Header("Mumble (lines without a recording)")]
    [Tooltip("Loops quietly while a line with no recording is in their speech bubble.")]
    public AudioClip mumble;
    [Range(0f, 1f)] public float mumbleVolume = 0.1f;
    [Tooltip("Metres: full volume within half a metre, fading to silent at this distance.")]
    public float mumbleRange = 3.5f;

    public AudioClip ClipFor(string text)
    {
        if (lines != null)
            foreach (var line in lines)
                if (line.text == text) return line.clip;
        return null;
    }

    public static NpcVoice Load(string name)
    {
        var voice = Resources.Load<NpcVoice>("Voices/" + name);
        // Load the (long) mumble up front, not mid-game the first time someone talks.
        if (voice != null && voice.mumble != null && voice.mumble.loadState == AudioDataLoadState.Unloaded)
            voice.mumble.LoadAudioData();
        return voice;
    }
}
