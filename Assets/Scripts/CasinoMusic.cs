using System.Collections.Generic;
using UnityEngine;

// Fixes "the music plays several times at once".
//
// The casino has a row of Speaker objects that each play the same looping
// music clip on their own AudioSource. Each one starts whenever its copy of
// the clip happens to be ready, so they run slightly out of step and you
// hear the track layered over itself as an echo. Nine decoders of a
// half-hour track is also far more than a headset should be doing.
//
// So at runtime the speakers are silenced and ONE source plays the track
// instead. It sits at a blend of the speakers' positions weighted toward
// the ones nearest to you: walk up to a speaker and the music is right
// there and loud; stand between two and it sits between them. One copy of
// the track, never out of sync with itself.
//
// Nothing in the scene is changed - the speakers keep their AudioSources
// and still decide the clip and the volume. Runs by itself in every scene:
// any group of two or more looping, play-on-awake sources sharing one long
// clip is treated as a set of speakers.
public sealed class CasinoMusic : MonoBehaviour
{
    const float MinimumMusicLength = 20f; // shorter loops are sound effects, not music

    class Track
    {
        public AudioSource Source;
        public readonly List<Transform> Speakers = new List<Transform>();
    }

    readonly List<Track> tracks = new List<Track>();
    Transform listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
        Install();
    }

    static void SceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Install();

    static void Install()
    {
        if (FindAnyObjectByType<CasinoMusic>() != null) return;

        var groups = new Dictionary<AudioClip, List<AudioSource>>();
        foreach (var source in FindObjectsByType<AudioSource>())
        {
            if (!source.enabled || !source.loop || !source.playOnAwake || source.clip == null) continue;
            if (source.clip.length < MinimumMusicLength) continue;
            if (!groups.TryGetValue(source.clip, out var list)) groups[source.clip] = list = new List<AudioSource>();
            list.Add(source);
        }

        CasinoMusic music = null;
        foreach (var pair in groups)
        {
            if (pair.Value.Count < 2) continue;
            if (music == null) music = new GameObject("Casino Music").AddComponent<CasinoMusic>();
            music.TakeOver(pair.Key, pair.Value);
        }
    }

    void TakeOver(AudioClip clip, List<AudioSource> speakers)
    {
        var first = speakers[0];
        var track = new Track();
        float volume = 0f;
        foreach (var speaker in speakers)
        {
            volume = Mathf.Max(volume, speaker.volume);
            track.Speakers.Add(speaker.transform);
            speaker.Stop();
            speaker.enabled = false;
        }

        var go = new GameObject("Music - " + clip.name);
        go.transform.SetParent(transform, false);
        go.transform.position = first.transform.position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = volume;
        source.priority = 0; // never the one that gets dropped when many sounds play
        source.spatialBlend = first.spatialBlend;
        source.rolloffMode = first.rolloffMode;
        source.minDistance = Mathf.Max(first.minDistance, 1.5f);
        source.maxDistance = first.maxDistance;
        source.dopplerLevel = 0f; // the source glides between speakers; that must not bend the pitch
        source.outputAudioMixerGroup = first.outputAudioMixerGroup;
        source.Play();

        track.Source = source;
        tracks.Add(track);
    }

    void LateUpdate()
    {
        if (listener == null)
        {
            var found = FindAnyObjectByType<AudioListener>();
            if (found == null) return;
            listener = found.transform;
        }

        Vector3 ear = listener.position;
        foreach (var track in tracks)
        {
            if (track.Source == null) continue;
            Vector3 sum = Vector3.zero;
            float total = 0f;
            foreach (var speaker in track.Speakers)
            {
                if (speaker == null) continue;
                // Strongly favours the nearest speakers.
                float weight = 1f / ((speaker.position - ear).sqrMagnitude + 0.5f);
                weight *= weight;
                sum += speaker.position * weight;
                total += weight;
            }
            if (total <= 0f) continue;
            Vector3 target = sum / total;
            track.Source.transform.position = Vector3.Lerp(track.Source.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }
    }
}
