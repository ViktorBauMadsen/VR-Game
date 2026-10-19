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
//
// Re-rack: once per team per game, the moment a team is down to 3 cups
// they slide into a tight 2-1 triangle at the front of the rack (the
// original middle pair + front cup spots, pointing at the opponent).
//
// Scoreboard: world-space text (3D TextMeshPro and world-space canvases
// alike) renders away from where it actually is in this project, so - same
// fix as the slot machine screen - the score is drawn by a small offscreen
// camera into a RenderTexture, which is shown on plain opaque quads lying on
// the table (scoreDisplays). The camera only renders for a couple of frames
// whenever the text changes.
public class BeerPongTable : MonoBehaviour
{
    [SerializeField] string[] teamNames = { "RED", "BLUE" };
    [SerializeField] Color[] teamColors = { new Color(0.9f, 0.15f, 0.15f), new Color(0.2f, 0.45f, 1f) };
    [SerializeField] float resetDelay = 4f;

    [Header("Re-rack")]
    [SerializeField] bool autoReRack = true;
    [SerializeField] int reRackAtCupsLeft = 3;
    [SerializeField] float reRackSlideSeconds = 0.5f;

    [Header("Scoreboard")]
    [SerializeField] Renderer[] scoreDisplays;
    [SerializeField] Color scoreBackground = new Color(0.02f, 0.05f, 0.035f);
    [SerializeField] Vector2Int scoreTextureSize = new Vector2Int(1024, 384);

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
    readonly bool[] _reRacked = new bool[2];

    // Rack slots in build order (see SetupBeerPong.BuildCupTriangle): back
    // row 0,1,2 (3 cups), middle row 3,4, front cup 5. A re-rack always
    // goes to the front of the rack: the middle pair + the front cup.
    static readonly int[] ReRackSlots = { 3, 4, 5 };

    RenderTexture _scoreTexture;
    Camera _scoreCamera;
    TextMeshProUGUI _scoreText;
    int _scoreRenderFramesLeft;

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

        BuildScoreRenderer();
    }

    void Start() => UpdateScoreboards();

    void OnDestroy()
    {
        if (_scoreTexture != null) _scoreTexture.Release();
    }

    // The camera sits 5 km below the table with a 2 m far plane, so the only
    // thing it can see is its own canvas - no layer setup needed - and the
    // canvas is beyond the player camera's far clip plane, so it never shows
    // up in the headset itself.
    void BuildScoreRenderer()
    {
        _scoreTexture = new RenderTexture(scoreTextureSize.x, scoreTextureSize.y, 16, RenderTextureFormat.ARGB32)
        {
            name = "Beer Pong Score",
            antiAliasing = 4,
        };

        var camGo = new GameObject("Scoreboard Camera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = new Vector3(0f, -5000f, 0f);
        _scoreCamera = camGo.AddComponent<Camera>();
        _scoreCamera.clearFlags = CameraClearFlags.SolidColor;
        _scoreCamera.backgroundColor = scoreBackground;
        _scoreCamera.orthographic = true;
        _scoreCamera.orthographicSize = 1f;
        _scoreCamera.nearClipPlane = 0.1f;
        _scoreCamera.farClipPlane = 2f;
        _scoreCamera.stereoTargetEye = StereoTargetEyeMask.None; // mono into the texture, not the headset
        _scoreCamera.targetTexture = _scoreTexture;
        _scoreCamera.aspect = (float)scoreTextureSize.x / scoreTextureSize.y; // don't inherit the Game view's aspect
        _scoreCamera.enabled = false;

        var canvasGo = new GameObject("Scoreboard Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(camGo.transform, false);
        canvasGo.layer = camGo.layer;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _scoreCamera;
        canvas.planeDistance = 1f;
        _scoreCamera.cullingMask = 1 << canvasGo.layer;

        var textGo = new GameObject("Score Text", typeof(RectTransform));
        textGo.layer = canvasGo.layer;
        var rect = (RectTransform)textGo.transform;
        rect.SetParent(canvasGo.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 16f);
        rect.offsetMax = new Vector2(-24f, -16f);

        _scoreText = textGo.AddComponent<TextMeshProUGUI>();
        _scoreText.alignment = TextAlignmentOptions.Center;
        _scoreText.fontStyle = FontStyles.Bold;
        // No wrapping: with it on, auto-size prefers breaking "RED 0 - 0 BLUE"
        // across two lines over shrinking it. The win message has its own \n.
        _scoreText.textWrappingMode = TextWrappingModes.NoWrap;
        _scoreText.enableAutoSizing = true;
        _scoreText.fontSizeMin = 20f;
        _scoreText.fontSizeMax = 170f;
        _scoreText.raycastTarget = false;

        // A runtime material copy rather than a MaterialPropertyBlock - this
        // project uses the GPU Resident Drawer, which doesn't honour those.
        Material screenMat = null;
        foreach (var display in scoreDisplays)
        {
            if (display == null) continue;
            if (screenMat == null)
            {
                screenMat = new Material(display.sharedMaterial) { name = "Beer Pong Score (runtime)" };
                screenMat.SetTexture("_BaseMap", _scoreTexture);
                if (screenMat.HasProperty("_MainTex")) screenMat.SetTexture("_MainTex", _scoreTexture);
            }
            display.sharedMaterial = screenMat;
        }
    }

    // Render the score camera only while the text has just changed - a
    // couple of frames covers TMP rebuilding its mesh before the draw.
    void LateUpdate()
    {
        if (_scoreCamera == null) return;
        _scoreCamera.enabled = _scoreRenderFramesLeft > 0;
        if (_scoreRenderFramesLeft > 0) _scoreRenderFramesLeft--;
    }

    public void OnCupHit(BeerPongCup cup)
    {
        if (_gameOver) return;

        int scoringTeam = 1 - cup.Side;
        Vector3 position = cup.transform.position;
        cup.Remove();

        AudioSource.PlayClipAtPoint(_popClip, position);
        SpawnBurst(position + Vector3.up * 0.1f, teamColors[scoringTeam]);
        onCupHit.Invoke(scoringTeam);

        int left = CupsStanding(cup.Side);
        if (left == 0)
        {
            Win(scoringTeam);
            return;
        }

        UpdateScoreboards();
        if (autoReRack && !_reRacked[cup.Side] && left == reRackAtCupsLeft)
            ReRack(cup.Side);
    }

    void ReRack(int side)
    {
        var rack = new System.Collections.Generic.List<BeerPongCup>();
        foreach (var c in _cups)
            if (c.Side == side) rack.Add(c); // hierarchy order = slot order
        if (rack.Count != 6) return; // only know the formations for a full 3-2-1 rack

        var standing = rack.FindAll(c => c.IsStanding);
        if (standing.Count != 3) return;
        _reRacked[side] = true;

        // Try every way of assigning the 3 cups to the 3 front slots and keep
        // whichever moves the cups least in total (so a cup already in one
        // of those slots stays put).
        int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
        Vector3[] best = null;
        float bestCost = float.MaxValue;
        foreach (var order in orders)
        {
            float cost = 0f;
            for (int i = 0; i < 3; i++)
                cost += Vector3.Distance(standing[i].transform.localPosition, rack[ReRackSlots[order[i]]].HomeLocalPosition);
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = new Vector3[3];
            for (int i = 0; i < 3; i++)
                best[i] = rack[ReRackSlots[order[i]]].HomeLocalPosition;
        }

        if (bestCost < 0.001f) return; // already a tight triangle
        StartCoroutine(SlideCups(standing, best));
    }

    IEnumerator SlideCups(System.Collections.Generic.List<BeerPongCup> cups, Vector3[] targets)
    {
        var starts = new Vector3[cups.Count];
        for (int i = 0; i < cups.Count; i++) starts[i] = cups[i].transform.localPosition;

        for (float t = 0f; t < 1f; t += Time.deltaTime / reRackSlideSeconds)
        {
            float eased = Mathf.SmoothStep(0f, 1f, t);
            for (int i = 0; i < cups.Count; i++)
                cups[i].transform.localPosition = Vector3.Lerp(starts[i], targets[i], eased);
            yield return null;
        }
        for (int i = 0; i < cups.Count; i++)
            cups[i].transform.localPosition = targets[i];
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
        _reRacked[0] = _reRacked[1] = false;
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
        string Colored(int t, string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(teamColors[t])}>{s}</color>";
        SetScoreboards($"{Colored(0, $"{teamNames[0]} {Score(0)}")} <color=#FFFFFF>-</color> {Colored(1, $"{Score(1)} {teamNames[1]}")}");
    }

    void SetScoreboards(string text)
    {
        if (_scoreText == null) return;
        _scoreText.text = text;
        _scoreRenderFramesLeft = 2;
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
