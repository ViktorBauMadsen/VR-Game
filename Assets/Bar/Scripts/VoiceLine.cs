using UnityEngine;

// A line of dialogue: the text shown in a speech bubble and, optionally, the
// recorded voice. With a clip the bubble stays up for the clip's length plus
// a short hold; without one it's timed from the text length.
[System.Serializable]
public struct VoiceLine
{
    public string text;
    public AudioClip clip;
    public VoiceLine(string text) { this.text = text; clip = null; }
}
