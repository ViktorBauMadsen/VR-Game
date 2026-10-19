using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Game rules for one beer pong table: each team defends 6 cups (3-2-1
// triangle) at its end. A hit cup is removed and scores for the other team;
// the first team to clear all 6 of the opponent's cups wins, the result
// shows on the scoreboards, and after resetDelay every cup comes back and
// both balls return to their spawn points.
//
// Feedback on a hit is a pop sound + a burst of particles in the scoring
// team's colour; a win plays a short fanfare. Both sounds are synthesised at
// runtime so the table has no audio asset dependencies. onCupHit/onGameWon
// are exposed for hooking up anything extra in the Inspector.
public class BeerPongTable : MonoBehaviour
{
    [SerializeField] string[] teamNames = { "RED", "BLUE" };
    [SerializeField] Color[] teamColors = { new Color(0.9f, 0.15f, 0.15f), new Color(0.2f, 0.45f, 1f) };
    [SerializeField] TextMeshPro[] scoreboards;
    [SerializeField] float resetDelay = 4f;

    [Tooltip("Invoked with the index of the team that scored (0 = Red, 1 = Blue).")]
    public UnityEvent<int> onCupHit;
    [Tooltip("Invoked with the index of the winning team (0 = Red, 1 = Blue).")]
    public UnityEvent<int> onGameWon;

    BeerPongCup[] _cups;
    BeerPongBall[] _balls;
    AudioSource _audio;
    AudioClip _popClip;
    AudioClip _winClip;
    bool _gameOver;

    void Awake()
    {
        _cups = GetComponentsInChildren<BeerPongCup>(true);
        _balls = GetComponentsInChildren<BeerPongBall>(true);

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        _audio.maxDistance = 15f;
        _popClip = CreatePopClip();
        _winClip = CreateWinClip();
    }

    void Start() => UpdateScoreboards();

    public void OnCupHit(BeerPongCup cup)
    {
        if (_gameOver) return;

        int scoringTeam = 1 - cup.Side;
        Vector3 position = cup.transform.position;
        cup.Remove();

        AudioSource.PlayClipAtPoint(_popClip, position);
        SpawnBurst(position + Vector3.up * 0.1f, teamColors[scoringTeam]);
        onCupHit.Invoke(scoringTeam);

        if (CupsStanding(cup.Side) == 0)
            Win(scoringTeam);
        else
            UpdateScoreboards();
    }

    void Win(int team)
    {
        _gameOver = true;
        SetScoreboards($"<color=#{ColorUtility.ToHtmlStringRGB(teamColors[team])}>{teamNames[team]} WINS!</color>\n<size=60%>New game in {resetDelay:0} s</size>");
        _audio.PlayOneShot(_winClip);
        onGameWon.Invoke(team);
        StartCoroutine(ResetAfterDelay());
    }

    IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);
        ResetGame();
    }

    public void ResetGame()
    {
        StopAllCoroutines();
        foreach (var cup in _cups) cup.Restore();
        foreach (var ball in _balls) ball.Respawn();
        _gameOver = false;
        UpdateScoreboards();
    }

    int CupsStanding(int side)
    {
        int count = 0;
        foreach (var cup in _cups)
            if (cup.Side == side && cup.IsStanding) count++;
        return count;
    }

    int CupsTotal(int side)
    {
        int count = 0;
        foreach (var cup in _cups)
            if (cup.Side == side) count++;
        return count;
    }

    // A team's score is how many of the *opponent's* cups are gone.
    int Score(int team) => CupsTotal(1 - team) - CupsStanding(1 - team);

    void UpdateScoreboards()
    {
        string Team(int t) => $"<color=#{ColorUtility.ToHtmlStringRGB(teamColors[t])}>{teamNames[t]} {Score(t)}</color>";
        SetScoreboards($"{Team(0)}  -  {Team(1)}");
    }

    void SetScoreboards(string text)
    {
        foreach (var board in scoreboards)
            if (board != null) board.text = text;
    }

    static void SpawnBurst(Vector3 position, Color color)
    {
        var go = new GameObject("Cup Hit Burst");
        go.SetActive(false); // configure before it starts playing - duration can't change while playing
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.8f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 0.8f;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.05f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // hemisphere opens upward

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
            go.GetComponent<ParticleSystemRenderer>().material = new Material(shader);

        go.SetActive(true);
    }

    // A short descending "plop".
    static AudioClip CreatePopClip()
    {
        const int rate = 44100;
        const float length = 0.15f;
        var data = new float[(int)(rate * length)];
        float phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / data.Length;
            phase += 2f * Mathf.PI * Mathf.Lerp(900f, 250f, t) / rate;
            data[i] = Mathf.Sin(phase) * (1f - t) * (1f - t) * 0.6f;
        }
        var clip = AudioClip.Create("Cup Pop", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Rising C-E-G-C arpeggio.
    static AudioClip CreateWinClip()
    {
        const int rate = 44100;
        const float noteLength = 0.16f;
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        int noteSamples = (int)(rate * noteLength);
        var data = new float[noteSamples * notes.Length + noteSamples * 2]; // let the last note ring
        for (int n = 0; n < notes.Length; n++)
        {
            int length = n == notes.Length - 1 ? noteSamples * 3 : noteSamples;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Exp(-t * (n == notes.Length - 1 ? 3f : 8f));
                float wave = Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t);
                data[n * noteSamples + i] += wave * envelope * 0.35f;
            }
        }
        var clip = AudioClip.Create("Beer Pong Win", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
