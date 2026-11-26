using UnityEngine;

// How every character's recorded voice is played (bartender, croupier,
// blackjack dealer, Bond, the mafia): half the volume the voices were mixed
// at, carrying only a third as far, and fading naturally with distance
// (SpatialAudio's curve) so you don't hear staff from across the hall.
// Voices also get a high priority, so the many looping machine sounds can
// never steal their channel.
public static class CharacterVoice
{
    // Multiplies each character's own voice volume (1 in the Inspector -> 0.5).
    public const float VolumeScale = 0.5f;
    // Full volume face to face, within this many metres.
    public const float Near = 1f;
    // The old audible ranges (12-20 m) divided by 3.
    public const float Far = 14f / 3f;

    // A voice source on `mouth` (the head), set up as above.
    public static AudioSource Create(Transform mouth, float far = Far)
    {
        var source = mouth.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.priority = 16; // lower = more important; effects are at the default 128
        SpatialAudio.Setup(source, Near, far);
        return source;
    }

    // Cuts off whatever was playing and says `clip`.
    public static void Play(AudioSource source, AudioClip clip, float volume = 1f)
    {
        if (source == null || clip == null) return;
        source.Stop();
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        source.clip = clip;
        source.volume = volume * VolumeScale;
        source.Play();
    }

    // Loads the clips now so the first time each line is said it isn't
    // decoded mid-game (or missed).
    public static void Preload(params VoiceLine[][] sets)
    {
        foreach (var set in sets)
        {
            if (set == null) continue;
            foreach (var line in set)
                if (line.clip != null && line.clip.loadState == AudioDataLoadState.Unloaded) line.clip.LoadAudioData();
        }
    }
}
