using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What the player sees of the run without opening the menu: a status line
// under the balance (day, team cash vs quota, clock), big announcements in
// the middle of the view when a day ends, and getting shot.
//
// It adds itself to the existing PlayerHUD canvas (the one the balance text
// lives on), so it sits in the same place and uses the same always-on-top
// text material. Sounds are generated in code, like the bar's.
public sealed class CasinoRunHud : MonoBehaviour
{
    static readonly Color Gold = new Color(1f, 0.82f, 0.29f);
    static readonly Color Good = new Color(0.36f, 1f, 0.48f);
    static readonly Color Bad = new Color(1f, 0.35f, 0.3f);

    CasinoRun run;
    TextMeshProUGUI line;
    Banner banner;
    GameObject balanceObject;
    bool pocketsWereVisible, hudHidden;
    Image fade;

    // The big announcements in the middle of your view. The PlayerHUD
    // canvas is drawn off toward the lower right of where it really is
    // (the balance was placed by eye to make up for that), so anything put
    // at "the centre" of it ends up out of sight. This is a text quad fixed
    // in front of the camera instead - the same kind of sign as the tables',
    // which is drawn exactly where it is. It sits just nearer than the
    // black-out, so it also reads on the black screen.
    class Banner
    {
        readonly WorldTextPanel panel;
        string value = "", shown;
        Color tint = Color.white;
        bool lowered;

        public Banner(Transform camera)
        {
            panel = WorldTextPanel.Create("Run Banner", camera, Vector3.zero, Vector2.one, new Vector2Int(1280, 640),
                Color.white, new Color(0.02f, 0.02f, 0.03f), 120f);
            Place();
            panel.Visible = false;
        }

        public string text { get => value; set { this.value = value ?? ""; Apply(); } }
        public Color color { get => tint; set { tint = value; Apply(); } }

        // A thin strip low in the view (subtitles) instead of the big card.
        public bool low { get => lowered; set { lowered = value; Place(); } }

        void Place()
        {
            // (Same 2:1 shape both ways - the text is drawn for that shape.)
            panel.transform.localPosition = lowered ? new Vector3(0f, -0.34f, 0.9f) : new Vector3(0f, 0.06f, 0.9f);
            panel.transform.localScale = lowered ? new Vector3(0.4f, 0.2f, 1f) : new Vector3(0.8f, 0.4f, 1f);
        }

        void Apply()
        {
            bool visible = value.Length > 0 && tint.a > 0.05f;
            panel.Visible = visible;
            if (!visible) return;
            string rich = $"<color=#{ColorUtility.ToHtmlStringRGB(tint)}>{value}</color>";
            if (rich == shown) return;
            shown = rich;
            panel.SetText(rich);
        }
    }

    // Everything on screen that isn't the scene: balance, status line, chip pockets.
    void SetHudVisible(bool visible)
    {
        if (visible == !hudHidden) return;
        hudHidden = !visible;
        if (balanceObject != null) balanceObject.SetActive(visible);
        if (line != null) line.gameObject.SetActive(visible);
        var pockets = FindAnyObjectByType<PlayerTorsoAnchor>();
        if (pockets == null) return;
        if (!visible)
        {
            pocketsWereVisible = pockets.PocketsVisible;
            pockets.SetPocketsVisible(false);
        }
        else if (pocketsWereVisible) pockets.SetPocketsVisible(true);
    }
    AudioSource audioSource, ambience, carSource;
    AudioClip gunshot, beep, cash;
    float bangAt; // seconds into `gunshot` where the bang is
    MafiaSounds sounds;
    Coroutine showing;
    float nextSetupAttempt, firstSetupAttempt = -1f, nextRefresh;
    int lastBeepSecond = -1;

    void Start()
    {
        run = GetComponent<CasinoRun>();
        if (run == null) run = CasinoRun.Instance;
        if (run != null) run.Happened += OnHappened;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;
        ambience = gameObject.AddComponent<AudioSource>();
        ambience.spatialBlend = 0f;
        ambience.playOnAwake = false;
        ambience.loop = true;
        ambience.volume = 0.35f;
        carSource = gameObject.AddComponent<AudioSource>();
        carSource.spatialBlend = 0f;
        carSource.playOnAwake = false;
        sounds = MafiaSounds.Instance;
        gunshot = sounds != null && sounds.gunshot != null ? sounds.gunshot : Gunshot();
        // The recorded shot starts with the gun being cocked; the bang comes this far in.
        bangAt = sounds != null && sounds.gunshot != null ? Mathf.Clamp(sounds.gunshotBangAt, 0f, gunshot.length) : 0f;
        // Loaded now, so the first shot isn't late while the clip loads.
        gunshot.LoadAudioData();
        if (sounds != null)
            foreach (var clip in new[] { sounds.comingWithMe, sounds.endOfTheLine, sounds.car, sounds.hitGround, sounds.crickets })
                if (clip != null) clip.LoadAudioData();
        beep = Tone("Beep", 0.09f, 880f, 880f);
        cash = Tone("Cash", 0.45f, 1320f, 1760f);
    }

    void OnDestroy()
    {
        if (run != null) run.Happened -= OnHappened;
        MafiaHit.Dismiss();
        KidnapScene.Exit();
    }

    // Back from the desert scene to where you were taken (if you're there).
    void LeaveDesert()
    {
        if (ambience != null) ambience.Stop();
        if (carSource != null) carSource.Stop();
        KidnapScene.Exit();
    }

    // ------------------------------------------------------------- set-up

    void TrySetup()
    {
        if (Time.unscaledTime < nextSetupAttempt) return;
        nextSetupAttempt = Time.unscaledTime + 0.5f;
        if (firstSetupAttempt < 0f) firstSetupAttempt = Time.unscaledTime;

        TextMeshProUGUI template = null;
        Canvas canvas = null;
        var balanceText = FindAnyObjectByType<PlayerHUDBalanceDisplay>();
        if (balanceText != null)
        {
            template = balanceText.GetComponent<TextMeshProUGUI>();
            canvas = balanceText.GetComponentInParent<Canvas>();
        }
        if (canvas == null)
        {
            // No PlayerHUD in this scene: after a moment, make a small one on the camera.
            if (Time.unscaledTime - firstSetupAttempt < 2f || Camera.main == null) return;
            var go = new GameObject("Run HUD", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(Camera.main.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1f);
            go.transform.localScale = Vector3.one * 0.001f;
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(800f, 450f);
        }

        var root = (RectTransform)canvas.transform;

        fade = new GameObject("Run Fade", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        fade.gameObject.layer = root.gameObject.layer;
        fade.rectTransform.SetParent(root, false);
        fade.rectTransform.SetAsFirstSibling();
        fade.rectTransform.sizeDelta = new Vector2(12000f, 12000f);
        fade.raycastTarget = false;
        fade.color = Color.clear;
        fade.enabled = false;

        line = Text("Run Status", root, template);
        line.rectTransform.anchorMin = line.rectTransform.anchorMax = line.rectTransform.pivot = new Vector2(0f, 1f);
        Vector2 under = template != null ? template.rectTransform.anchoredPosition + new Vector2(0f, -62f) : new Vector2(20f, -82f);
        line.rectTransform.anchoredPosition = under;
        line.rectTransform.sizeDelta = new Vector2(760f, 80f);
        line.fontSize = 26f;
        line.alignment = TextAlignmentOptions.TopLeft;
        line.textWrappingMode = TextWrappingModes.NoWrap;

        if (template != null) balanceObject = template.gameObject;
        var view = Camera.main != null ? Camera.main.transform : canvas.transform.parent;
        banner = new Banner(view != null ? view : canvas.transform);
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TextMeshProUGUI template)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (template != null)
        {
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial; // the always-on-top one
        }
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

    // ------------------------------------------------------------ updates

    void Update()
    {
        if (line == null) { TrySetup(); if (line == null) return; }
        if (run == null) return;

        if (run.Phase == RunPhase.Running && run.TimeLeft <= 10f && run.TimeLeft > 0f)
        {
            int second = Mathf.CeilToInt(run.TimeLeft);
            if (second != lastBeepSecond)
            {
                lastBeepSecond = second;
                audioSource.PlayOneShot(beep, 0.35f);
            }
        }
        else lastBeepSecond = -1;

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;

        switch (run.Phase)
        {
            case RunPhase.Running:
                bool made = run.TeamCash >= run.Quota;
                string cashColour = ColorUtility.ToHtmlStringRGB(made ? Good : Gold);
                string clockColour = ColorUtility.ToHtmlStringRGB(run.TimeLeft < 30f ? Bad : Color.white);
                string who = run.Players > 1 ? "TEAM " : "";
                string extras = "   TOKENS " + run.Tokens + (run.Vests > 0 ? "   VEST" : "")
                    + (run.GreedToday ? "   x2 TOKENS" : "") + (run.HotCharges > 0 ? "   HOT x" + run.HotCharges : "") + (run.CharmCharges > 0 ? "   CHARM x" + run.CharmCharges : "");
                line.text = $"DAY {run.Day}   <color=#{clockColour}>{Clock(run.TimeLeft)}</color>{extras}\n"
                    + $"<color=#{cashColour}>{who}${run.TeamCash:N0}</color> / ${run.Quota:N0} QUOTA" + EventLine();
                line.color = Color.white;
                break;
            case RunPhase.Gathering:
                line.text = $"DAY {run.Day}   {Clock(run.TimeLeft)}   QUOTA ${run.Quota:N0}\n"
                    + (run.LocalReady ? $"<color=#{ColorUtility.ToHtmlStringRGB(Good)}>READY</color>" : $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>PRESS THE DOOR</color>")
                    + $"   {run.ReadyCount}/{run.Players} READY";
                line.color = Color.white;
                break;
            case RunPhase.Dead:
                line.text = "";
                break;
            default:
                line.text = "HUB - FREE PLAY\n<size=75%>Y (left hand): menu / start a game</size>";
                line.color = new Color(1f, 1f, 1f, 0.6f);
                break;
        }
    }

    // "\nHAPPY HOUR" in green or red, or nothing on a quiet day.
    string EventLine(bool withText = false)
    {
        if (run.TodayEvent == DayEvent.None) return "";
        string colour = ColorUtility.ToHtmlStringRGB(CasinoRun.EventIsGood(run.TodayEvent) ? Good : Bad);
        return $"\n<size=80%><color=#{colour}>{CasinoRun.EventTitle(run.TodayEvent)}</color>"
            + (withText ? " - " + CasinoRun.EventText(run.TodayEvent) : "") + "</size>";
    }

    public static string Clock(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
    }

    void OnHappened(RunEvent what, int pay)
    {
        if (banner == null) return;
        string next = $"DAY {run.Day}\n<size=70%>QUOTA ${run.Quota:N0}  -  {Clock(run.TimeLeft)} ON THE CLOCK</size>";
        switch (what)
        {
            case RunEvent.Gathering:
                Show(Loading());
                break;
            case RunEvent.Started:
                Show(Announce("<size=60%>THE FAMILY WANTS ITS MONEY</size>\n" + next, Gold, 5f));
                break;
            case RunEvent.Survived:
                audioSource.PlayOneShot(cash, 0.5f);
                string paid = $"PAID UP. THE COLLECTOR TOOK ${pay:N0}.\nYOU EARNED {run.LastAward} TOKEN{(run.LastAward == 1 ? "" : "S")}.";
                // With a casino door, every new day starts back outside it.
                if (run.Phase == RunPhase.Gathering) Show(Loading(paid));
                else Show(Announce($"<size=60%>{paid}</size>\n" + next, Good, 7f));
                break;
            case RunEvent.VestSaved:
                if (run.Phase == RunPhase.Gathering)
                {
                    audioSource.PlayOneShot(gunshot, 0.9f);
                    Show(Loading("YOU CAME UP SHORT.\nTHE VEST TOOK THE BULLET."));
                }
                else Show(VestHit($"<size=60%>YOU CAME UP SHORT.\nTHE VEST TOOK THE BULLET.</size>\n" + next));
                break;
            case RunEvent.Dead:
                Show(Death(Mathf.Max(0, run.DiedOnDay - 1)));
                break;
            case RunEvent.Abandoned:
                Show(Announce("BACK IN THE HUB\n<size=60%>No quota, no collector.</size>", Color.white, 3f));
                break;
        }
    }

    void Show(IEnumerator routine)
    {
        if (showing != null) StopCoroutine(showing);
        MafiaHit.Dismiss(); // in case a death scene was cut short
        LeaveDesert();
        SetHudVisible(true);
        banner.low = false;
        fade.enabled = false;
        showing = StartCoroutine(routine);
    }

    IEnumerator Announce(string text, Color colour, float seconds)
    {
        banner.text = text;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float alpha = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((seconds - t) / 0.8f);
            banner.color = new Color(colour.r, colour.g, colour.b, alpha);
            yield return null;
        }
        banner.text = "";
        showing = null;
    }

    // "Start game": black out, move to the casino door behind the black, fade back in.
    IEnumerator Loading(string headline = "LOADING...")
    {
        fade.enabled = true;
        banner.text = "";
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(0f, 0f, 0f, t / 0.35f);
            yield return null;
        }
        fade.color = Color.black;
        banner.color = Gold;
        banner.text = $"<size=75%>{headline}</size>\n<size=55%>Meet your crew at the casino door.</size>";

        var entrance = RunEntrance.Instance;
        if (entrance != null) entrance.MovePlayerToSpawn(run.LocalSlot);
        yield return new WaitForSecondsRealtime(headline == "LOADING..." ? 2f : 3.5f);

        for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
        {
            float alpha = 1f - t / 0.8f;
            fade.color = new Color(0f, 0f, 0f, alpha);
            banner.color = new Color(Gold.r, Gold.g, Gold.b, alpha);
            yield return null;
        }
        fade.enabled = false;
        yield return Announce("PRESS THE DOOR WHEN YOU'RE READY\n"
            + $"<size=60%>DAY {run.Day} QUOTA ${run.Quota:N0}  -  THE CLOCK STARTS WHEN EVERYONE IS IN</size>"
            + (run.TodayEvent != DayEvent.None ? $"<size=75%>{EventLine(true)}</size>" : ""), Gold, run.TodayEvent != DayEvent.None ? 9f : 6f);
    }

    IEnumerator VestHit(string text)
    {
        audioSource.PlayOneShot(gunshot, 0.9f);
        yield return new WaitForSecondsRealtime(bangAt);
        fade.enabled = true;
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(1f, 1f, 1f, 1f - t / 0.6f);
            yield return null;
        }
        fade.enabled = false;
        yield return Announce(text, Gold, 6f);
    }

    // Seconds after the run is lost (on every headset alike) that the hood
    // comes off out in the desert, and that he starts his line to the first
    // in the row; then he shoots, and moves on to the next ShotInterval later.
    const float RevealAt = 14.5f, FirstShotAt = RevealAt + 6.5f, ShotInterval = 3f;
    // How long a subtitle stays up after its voice line ends.
    const float SubtitleHold = 0.5f;

    // One of the hitman's lines: spoken from him, shown as a subtitle until
    // shortly after it ends. Returns when the line ends.
    float HitmanSays(AudioClip clip, string subtitle, float fallbackSeconds)
    {
        banner.color = Bad;
        banner.text = subtitle;
        MafiaHit.Speak(clip);
        float end = Time.unscaledTime + (clip != null ? clip.length : fallbackSeconds);
        StartCoroutine(ClearSubtitle(subtitle, end + SubtitleHold));
        return end;
    }

    IEnumerator ClearSubtitle(string subtitle, float at)
    {
        while (Time.unscaledTime < at) yield return null;
        if (banner.text == subtitle) banner.text = "";
    }

    IEnumerator FadeOut(AudioSource source, float seconds)
    {
        float volume = source.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            source.volume = volume * (1f - t / seconds);
            yield return null;
        }
        source.Stop();
        source.volume = volume;
    }
    static readonly Color Hood = new Color(0.035f, 0.025f, 0.018f);
    static readonly Color Muffled = new Color(0.75f, 0.72f, 0.68f);

    // Losing a run: the hitman walks up, pulls a hood over your head, and
    // you (and the rest of the team) are driven out into the desert. The
    // hood comes off standing in a line in front of a pit, the car's
    // headlights in your eyes, and he works his way down the line.
    IEnumerator Death(int daysSurvived)
    {
        float start = Time.unscaledTime;
        // Nothing between you and the hitman: balance, status line and chip
        // pockets are put away (the menu closes itself), and what he says
        // is a thin subtitle low in the view rather than a card in the middle.
        SetHudVisible(false);
        banner.low = true;
        banner.color = Bad;
        banner.text = "THE COLLECTOR HAS RUN OUT OF PATIENCE...";

        var player = Camera.main != null ? Camera.main.transform : null;
        if (player == null)
        {
            yield return new WaitForSecondsRealtime(1.6f);
            audioSource.PlayOneShot(gunshot, 1f);
            yield return new WaitForSecondsRealtime(bangAt);
            yield return ShotAndEnd(daysSurvived);
            yield break;
        }

        // ----- taken
        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        float floor = origin != null ? origin.transform.position.y : player.position.y - 1.6f;
        yield return MafiaHit.Approach(player, floor);
        // He stops in front of you, says his piece, then steps in with the hood.
        float lineEnd = HitmanSays(sounds != null ? sounds.comingWithMe : null, "\"YOU'RE COMING WITH ME!\"", 2f);
        yield return MafiaHit.Act(() => player.position, Mathf.Max(0.6f, lineEnd - 0.9f - Time.unscaledTime), 0f, 0f);
        yield return MafiaHit.WalkTo(() => player.position, 0.62f, 1.5f, () => player.position);
        yield return MafiaHit.Act(() => player.position, 0.55f, 0f, 1f);

        // The hood goes on.
        audioSource.PlayOneShot(KidnapScene.Thump, 1f);
        fade.enabled = true;
        for (float t = 0f; t < 0.18f; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(Hood.r, Hood.g, Hood.b, t / 0.18f);
            yield return null;
        }
        fade.color = Hood;
        banner.text = "";
        MafiaHit.Dismiss();
        KidnapScene.Enter(); // you're moved while you can't see

        yield return new WaitForSecondsRealtime(0.5f);
        banner.color = Muffled;
        banner.text = "<i>[you're dragged into a car]</i>";
        if (sounds != null && sounds.car != null)
        {
            carSource.clip = sounds.car;
            carSource.volume = 0.8f;
            carSource.Play();
        }
        else
        {
            audioSource.PlayOneShot(KidnapScene.DoorSlam, 0.8f);
            carSource.clip = KidnapScene.Engine;
            carSource.volume = 0.75f;
            carSource.PlayDelayed(0.9f);
        }
        yield return new WaitForSecondsRealtime(2f);
        banner.text = "<i>[a long drive into the desert]</i>";

        // On a fixed clock, so the whole team's hoods come off together.
        // The car pulls up and you're thrown out onto the sand just before.
        float reveal = Mathf.Max(start + RevealAt, Time.unscaledTime + 3f);
        while (Time.unscaledTime < reveal - 2f) yield return null;
        yield return FadeOut(carSource, 0.6f);
        banner.text = "<i>[you're thrown out of the car]</i>";
        audioSource.PlayOneShot(sounds != null && sounds.hitGround != null ? sounds.hitGround : KidnapScene.Thump, 1f);
        while (Time.unscaledTime < reveal) yield return null;

        // ----- the desert
        MafiaHit.Spawn(KidnapScene.HitmanStart, KidnapScene.LineCentre, true);
        bool recorded = sounds != null && sounds.crickets != null;
        ambience.clip = recorded ? sounds.crickets : KidnapScene.Crickets;
        ambience.volume = recorded ? sounds.cricketsVolume : 0.35f;
        ambience.Play();
        banner.text = "";
        for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
        {
            float k = t / 1.2f;
            fade.color = new Color(Hood.r, Hood.g, Hood.b, 1f - k * k * (3f - 2f * k));
            yield return null;
        }
        fade.enabled = false;
        banner.text = "";

        // He says his line to each of you before he shoots (the same length
        // on every headset, so the shots stay in step). The gunshot clip is
        // started bangAt early - he cocks the gun after his line - so the
        // bang lands exactly with the flash and the hit.
        AudioClip lastWords = sounds != null ? sounds.endOfTheLine : null;
        float lineTime = (lastWords != null ? lastWords.length : 2f) + 0.25f;
        float late = reveal - (start + RevealAt); // only if this headset fell behind
        int n = KidnapScene.Order.Count;
        for (int k = 0; k < n; k++)
        {
            int victim = k;
            bool me = victim == KidnapScene.LocalIndex;
            float speakAt = start + late + FirstShotAt + (ShotInterval + lineTime + bangAt) * k;
            float fireAt = speakAt + lineTime + bangAt;
            Vector3 Head() => KidnapScene.HeadOf(victim);

            yield return MafiaHit.WalkTo(() => KidnapScene.StandPoint(victim), 0.05f, Mathf.Max(0.3f, speakAt - 0.9f - Time.unscaledTime), Head);
            yield return MafiaHit.Act(Head, 0.6f, 1f, 0f);
            yield return MafiaHit.Act(Head, Mathf.Max(0f, speakAt - Time.unscaledTime), 1f, 0f);
            HitmanSays(lastWords, "\"END OF THE LINE, PUNK!\"", 2f);
            yield return MafiaHit.Act(Head, Mathf.Max(0f, fireAt - bangAt - Time.unscaledTime), 1f, 0f);
            audioSource.PlayOneShot(gunshot, me ? 1f : 0.85f);
            yield return MafiaHit.Act(Head, Mathf.Max(0f, fireAt - Time.unscaledTime), 1f, 0f);
            if (me) break; // your turn: the bang is now

            MafiaHit.Fire();
            banner.text = "";
            yield return new WaitForSecondsRealtime(0.07f);
            MafiaHit.StopFlash();
            KidnapScene.Fell(victim);
            yield return MafiaHit.Act(Head, 0.4f, 0f, 0f);
        }
        MafiaHit.Fire();
        yield return ShotAndEnd(daysSurvived);
    }

    // The moment of the bang that kills you (the sound is already playing),
    // then the end screen; you're back where you were taken from when it fades.
    IEnumerator ShotAndEnd(int daysSurvived)
    {
        yield return new WaitForSecondsRealtime(0.07f); // the muzzle flash, for a moment
        fade.enabled = true;
        banner.text = "";
        banner.low = false;
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            // Muzzle flash, straight to red, then black.
            float k = t / 0.5f;
            fade.color = k < 0.15f ? Color.white : Color.Lerp(new Color(0.6f, 0f, 0f, 1f), Color.black, (k - 0.15f) / 0.85f);
            yield return null;
        }
        fade.color = Color.black;
        MafiaHit.Dismiss();

        string days = daysSurvived == 1 ? "1 DAY" : daysSurvived + " DAYS";
        // The end screen: how the run went, on the black.
        string top = run.Players > 1 ? $"\nTOP EARNER: {(run.TopEarnerIsMe ? "YOU" : "PLAYER " + (run.TopEarner + 1))}" : "";
        banner.text = $"THE FAMILY SENDS ITS REGARDS\n<size=60%>YOU LASTED {days}</size>\n"
            + $"<size=45%><color=#FFFFFF>BIGGEST WIN  ${run.StatBiggestWin:N0}\n"
            + $"TOTAL WINNINGS  ${run.StatWinnings:N0}  ({run.StatWins} WINS)\n"
            + $"MOST CASH HELD  ${run.StatPeakCash:N0}\n"
            + $"TOKENS EARNED  {run.StatTokensEarned}    POWERUPS BOUGHT  {run.StatPowerupsBought}{top}</color>\n"
            + "Open the menu (Y) to try again.</size>";
        banner.color = Bad;
        yield return new WaitForSecondsRealtime(9f);
        LeaveDesert();

        for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
        {
            float alpha = 1f - t / 1.5f;
            fade.color = new Color(0f, 0f, 0f, alpha);
            banner.color = new Color(Bad.r, Bad.g, Bad.b, alpha);
            yield return null;
        }
        fade.enabled = false;
        banner.text = "";
        SetHudVisible(true);
        showing = null;
    }

    // ------------------------------------------------------------- sounds

    static AudioClip Gunshot()
    {
        const int rate = 44100;
        var data = new float[(int)(rate * 0.7f)];
        var random = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.25f;
            float crack = noise * Mathf.Exp(-t * 60f);
            float boom = low * Mathf.Exp(-t * 9f) * 1.6f;
            float thump = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 14f);
            data[i] = Mathf.Clamp(crack + boom + thump, -1f, 1f);
        }
        var clip = AudioClip.Create("Gunshot", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // A short bell-like tone gliding from one pitch to another.
    static AudioClip Tone(string name, float seconds, float from, float to)
    {
        const int rate = 44100;
        var data = new float[(int)(rate * seconds)];
        float phase = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float k = (float)i / data.Length;
            phase += 2f * Mathf.PI * (k < 0.35f ? from : to) / rate;
            float envelope = Mathf.Min(1f, k * 40f) * Mathf.Exp(-k * 4f);
            data[i] = Mathf.Sin(phase) * envelope * 0.6f;
        }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
