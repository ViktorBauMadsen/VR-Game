using UnityEngine;

// Small synthesised sound effects for the bar, built on first use (same
// approach as the beer pong table's pop/fanfare) so no audio files are needed.
public static class BarSounds
{
    const int Rate = 44100;

    static AudioClip _ding, _gulp, _exhale;

    public static AudioClip Ding => _ding != null ? _ding : _ding = CreateDing();
    public static AudioClip Gulp => _gulp != null ? _gulp : _gulp = CreateGulp();
    public static AudioClip Exhale => _exhale != null ? _exhale : _exhale = CreateExhale();

    public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip != null) AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    // Two-note cash register "ka-ching".
    static AudioClip CreateDing()
    {
        var data = new float[(int)(Rate * 0.9f)];
        float[] notes = { 1568f, 2093f };
        for (int n = 0; n < notes.Length; n++)
        {
            int start = (int)(Rate * 0.09f * n);
            for (int i = start; i < data.Length; i++)
            {
                float t = (float)(i - start) / Rate;
                float wave = Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * notes[n] * 2.76f * t);
                data[i] += wave * Mathf.Exp(-t * 6f) * 0.25f;
            }
        }
        return Make("Bar Ding", data);
    }

    // A short low blip that rises in pitch, like a swallow.
    static AudioClip CreateGulp()
    {
        var data = new float[(int)(Rate * 0.14f)];
        float phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            phase += 2f * Mathf.PI * Mathf.Lerp(130f, 330f, t * t) / Rate;
            data[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t) * 0.7f;
        }
        return Make("Bar Gulp", data);
    }

    // Soft filtered noise that swells and fades - breathing out.
    static AudioClip CreateExhale()
    {
        var data = new float[(int)(Rate * 1.1f)];
        var random = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.12f;
            data[i] = low * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 1.5f) * 0.6f;
        }
        return Make("Bar Exhale", data);
    }

    static AudioClip Make(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
