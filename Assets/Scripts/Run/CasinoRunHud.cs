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
    TextMeshProUGUI line, banner;
    Image fade;
    AudioSource audioSource;
    AudioClip gunshot, beep, cash;
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
        gunshot = Gunshot();
        beep = Tone("Beep", 0.09f, 880f, 880f);
        cash = Tone("Cash", 0.45f, 1320f, 1760f);
    }

    void OnDestroy()
    {
        if (run != null) run.Happened -= OnHappened;
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

        banner = Text("Run Banner", root, template);
        banner.rectTransform.anchorMin = banner.rectTransform.anchorMax = banner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        banner.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        banner.rectTransform.sizeDelta = new Vector2(760f, 320f);
        banner.fontSize = 46f;
        banner.fontStyle = FontStyles.Bold;
        banner.alignment = TextAlignmentOptions.Center;
        banner.textWrappingMode = TextWrappingModes.Normal;
        banner.text = "";
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
        fade.enabled = true;
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(1f, 1f, 1f, 1f - t / 0.6f);
            yield return null;
        }
        fade.enabled = false;
        yield return Announce(text, Gold, 6f);
    }

    IEnumerator Death(int daysSurvived)
    {
        banner.text = "<size=70%>THE COLLECTOR HAS RUN OUT OF PATIENCE...</size>";
        banner.color = Bad;

        // The hitman walks up to you and raises his gun (see MafiaHit).
        var player = Camera.main != null ? Camera.main.transform : null;
        if (player != null)
        {
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            float floor = origin != null ? origin.transform.position.y : player.position.y - 1.6f;
            yield return MafiaHit.WalkUp(player, floor, line => banner.text = $"<size=55%>{line}</size>");
            MafiaHit.Fire();
        }
        else yield return new WaitForSecondsRealtime(1.6f);

        audioSource.PlayOneShot(gunshot, 1f);
        yield return new WaitForSecondsRealtime(0.07f); // the muzzle flash, for a moment
        fade.enabled = true;
        banner.text = "";
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

        for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
        {
            float alpha = 1f - t / 1.5f;
            fade.color = new Color(0f, 0f, 0f, alpha);
            banner.color = new Color(Bad.r, Bad.g, Bad.b, alpha);
            yield return null;
        }
        fade.enabled = false;
        banner.text = "";
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
