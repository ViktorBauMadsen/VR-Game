using UnityEngine;

// The mafia's recorded sounds and voice lines (in Assets/Audio/VoiceLines/Mafia).
// CasinoRunHud is added at runtime, so it can't hold clip references itself;
// it loads this asset from Resources/MafiaSounds instead. Any clip left empty
// falls back to the sound made in code.
[CreateAssetMenu(menuName = "Casino/Mafia Sounds")]
public sealed class MafiaSounds : ScriptableObject
{
    [Tooltip("When he stops in front of you in the casino: \"You're coming with me!\"")]
    public AudioClip comingWithMe;
    [Tooltip("Right before he shoots: \"End of the line, punk!\"")]
    public AudioClip endOfTheLine;
    public AudioClip gunshot;
    [Tooltip("Plays while you're hooded in the car.")]
    public AudioClip car;
    [Tooltip("You hitting the ground when you're thrown out of the car.")]
    public AudioClip hitGround;

    static MafiaSounds _instance;
    static bool _loaded;

    public static MafiaSounds Instance
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _instance = Resources.Load<MafiaSounds>("MafiaSounds");
            }
            return _instance;
        }
    }
}
