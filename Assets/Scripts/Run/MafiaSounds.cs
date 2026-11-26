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
    [Tooltip("Seconds into the gunshot clip where the bang is (anything before it - the gun being cocked - plays first). "
        + "The muzzle flash and the hit are timed to this. Tools > Casino > Mafia > Build Mafia Assets measures it.")]
    public float gunshotBangAt;
    [Tooltip("Plays while you're hooded in the car.")]
    public AudioClip car;
    [Tooltip("You hitting the ground when you're thrown out of the car.")]
    public AudioClip hitGround;
    [Tooltip("Loops in the background out in the desert.")]
    public AudioClip crickets;
    [Range(0f, 1f)] public float cricketsVolume = 0.35f;

    static MafiaSounds _instance;
    static bool _loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _loaded = false;
    }

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
