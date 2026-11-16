using System;
using System.Collections.Generic;
using System.Text;
using Casino.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// The one menu of the game, opened with Y on the left controller (M on a
// keyboard). It appears in front of you and stays where it is; walk away or
// press Y again and it closes. Three tabs:
//
//   RUN      start a run, see the day / quota / clock, pay up early
//   SHOP     buy powerups with your own cash
//   FRIENDS  host a game, or join one found on the same Wi-Fi (or by IP)
//
// Drawing and button handling live in MenuSurface; this only decides what is
// on each page. A page is rebuilt when something structural changes (tab,
// run phase, a button becoming available...) and its numbers are refreshed
// a few times a second in between.
[DefaultExecutionOrder(10020)]
public sealed class CasinoMenu : MonoBehaviour
{
    enum Tab { Run, Shop, Friends }

    static readonly Color Card = new Color(0.1f, 0.12f, 0.17f);
    static readonly Color Gold = new Color(1f, 0.82f, 0.29f);
    static readonly Color Dim = new Color(0.72f, 0.75f, 0.82f);
    static readonly Color Green = new Color(0.16f, 0.58f, 0.32f);
    static readonly Color Red = new Color(0.7f, 0.2f, 0.2f);
    static readonly Color Blue = new Color(0.15f, 0.38f, 0.62f);
    static readonly Color Grey = new Color(0.27f, 0.29f, 0.35f);
    static readonly Color GoodText = new Color(0.36f, 1f, 0.48f);
    static readonly Color BadText = new Color(1f, 0.42f, 0.36f);

    const float Left = 40f, Full = MenuSurface.Width - 80f, Top = 250f;

    MenuSurface surface;
    CasinoRun run;
    InputAction toggle;
    Tab tab = Tab.Run;
    bool keypad;
    string address = "";
    string signature;
    float nextRefresh;
    readonly List<Func<bool>> live = new List<Func<bool>>();
    readonly StringBuilder builder = new StringBuilder();

    CasinoMultiplayer Multiplayer => CasinoMultiplayer.Instance;
    bool Open => surface != null && surface.gameObject.activeSelf;

    void Start()
    {
        run = GetComponent<CasinoRun>();
        if (run == null) run = CasinoRun.Instance;

        var panel = new GameObject("Casino Menu");
        surface = panel.AddComponent<MenuSurface>();
        surface.Build();
        panel.SetActive(false);

        toggle = new InputAction("Casino Menu", InputActionType.Button);
        toggle.AddBinding("<XRController>{LeftHand}/secondaryButton");
        toggle.AddBinding("<Keyboard>/m");
        toggle.performed += OnToggle;
        toggle.Enable();

        if (run != null) run.Happened += OnHappened;
    }

    void OnDestroy()
    {
        toggle?.Dispose();
        if (run != null) run.Happened -= OnHappened;
        if (surface != null) Destroy(surface.gameObject);
    }

    void OnToggle(InputAction.CallbackContext context)
    {
        if (surface == null) return;
        if (Open) { surface.gameObject.SetActive(false); return; }
        var head = Camera.main != null ? Camera.main.transform : null;
        if (head == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
        surface.transform.SetPositionAndRotation(head.position + forward * 1.05f + Vector3.down * 0.12f,
            Quaternion.LookRotation(forward, Vector3.up));
        surface.gameObject.SetActive(true);
        signature = null; // redraw with fresh numbers
    }

    // Dying or a new day is worth seeing on the RUN tab next time it opens.
    void OnHappened(RunEvent what, int pay)
    {
        if (what == RunEvent.Dead || what == RunEvent.Started || what == RunEvent.Gathering) { tab = Tab.Run; keypad = false; }
        // You are about to be moved to the door - the menu would be left behind.
        bool moved = what == RunEvent.Gathering
            || ((what == RunEvent.Survived || what == RunEvent.VestSaved) && run.Phase == RunPhase.Gathering);
        if (moved && surface != null) surface.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!Open || run == null) return;

        var head = Camera.main != null ? Camera.main.transform : null;
        if (head != null && Vector3.Distance(head.position, surface.transform.position) > 4f)
        {
            surface.gameObject.SetActive(false);
            return;
        }

        string now = Signature();
        if (now != signature)
        {
            signature = now;
            Rebuild();
        }
        else if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.2f;
            bool changed = false;
            foreach (var refresh in live) changed |= refresh();
            if (changed) surface.MarkDirty();
        }
    }

    // Everything that decides which boxes and buttons a page has.
    string Signature()
    {
        var mp = Multiplayer;
        builder.Clear();
        builder.Append(tab).Append(keypad).Append(run.Phase).Append(run.Authority).Append(run.Day);
        switch (tab)
        {
            case Tab.Run:
                builder.Append(run.Phase == RunPhase.Running && run.TeamCash >= run.Quota).Append(run.Players);
                break;
            case Tab.Shop:
                for (int i = 0; i < 4; i++) builder.Append(run.WhyNot((Powerup)i)).Append(run.Price((Powerup)i)).Append('|');
                break;
            default:
                if (mp == null) break;
                builder.Append(mp.Ready).Append(mp.InSession).Append(mp.Joining).Append(mp.IsHost).Append(mp.Busy)
                    .Append(mp.Discovery != null ? mp.Discovery.Version : 0);
                break;
        }
        return builder.ToString();
    }

    // -------------------------------------------------------------- pages

    void Rebuild()
    {
        surface.Clear();
        live.Clear();

        surface.Text("CASINO", Left, 30f, 420f, 80f, 60f, Gold, TextAlignmentOptions.Left, true);
        Live(surface.Text("", 440f, 38f, 400f, 64f, 46f, Color.white, TextAlignmentOptions.Right, true),
            () => (run.Phase == RunPhase.Running || run.Phase == RunPhase.Gathering ? "<size=70%><color=#FFD24A>" + run.Tokens + " TOKENS</color></size>   " : "")
                + (PlayerBalance.Instance != null ? "$" + PlayerBalance.Instance.Balance.ToString("N0") : ""));
        surface.Button("X", MenuSurface.Width - 130f, 28f, 90f, 84f, Grey, () => surface.gameObject.SetActive(false));

        TabButton("RUN", Tab.Run, 0);
        TabButton("SHOP", Tab.Shop, 1);
        TabButton("FRIENDS", Tab.Friends, 2);

        switch (tab)
        {
            case Tab.Run: RunPage(); break;
            case Tab.Shop: ShopPage(); break;
            default:
                if (keypad) KeypadPage(); else FriendsPage();
                break;
        }

        foreach (var refresh in live) refresh();
        surface.MarkDirty();
    }

    void TabButton(string label, Tab target, int index)
    {
        float width = (Full - 40f) / 3f;
        surface.Button(label, Left + index * (width + 20f), 140f, width, 84f, tab == target ? Blue : Grey, () =>
        {
            tab = target;
            keypad = false;
        }, true, 36f);
    }

    void RunPage()
    {
        switch (run.Phase)
        {
            case RunPhase.Running:
            {
                surface.Box(Left, Top, Full, 470f, Card);
                surface.Text("DAY " + run.Day, Left, Top + 20f, Full, 110f, 96f, Gold, TextAlignmentOptions.Center, true);
                var clock = surface.Text("", Left, Top + 135f, Full, 90f, 72f, Color.white, TextAlignmentOptions.Center, true);
                Live(clock, () =>
                {
                    clock.color = run.TimeLeft < 30f ? BadText : Color.white;
                    return CasinoRunHud.Clock(run.TimeLeft) + "  LEFT";
                });

                surface.Box(Left + 50f, Top + 255f, Full - 100f, 46f, new Color(0.02f, 0.03f, 0.05f));
                var bar = surface.Box(Left + 50f, Top + 255f, Full - 100f, 46f, Green);
                var cash = surface.Text("", Left, Top + 320f, Full, 60f, 46f, Color.white, TextAlignmentOptions.Center, true);
                Live(cash, () =>
                {
                    float filled = run.Quota > 0 ? Mathf.Clamp01(run.TeamCash / (float)run.Quota) : 0f;
                    bar.rectTransform.sizeDelta = new Vector2(Mathf.Max(36f, (Full - 100f) * filled), 46f);
                    bar.color = filled >= 1f ? Green : Gold;
                    return (run.Players > 1 ? "TEAM CASH  $" : "CASH  $") + run.TeamCash.ToString("N0") + "  /  $" + run.Quota.ToString("N0");
                });
                Live(surface.Text("", Left, Top + 390f, Full, 50f, 30f, Dim), () =>
                    (run.Players > 1 ? run.Players + " players   " : "")
                    + (run.TeamCash >= run.Quota ? "Pay now: +" + run.TokensFor(run.TeamCash, run.Quota) + " tokens   " : "")
                    + (run.Vests > 0 ? "Vest on   " : "") + (run.CharmCharges > 0 ? "Lucky charm x" + run.CharmCharges : ""));

                surface.Text($"When the clock hits zero the collector checks your cash. Make the quota and he takes ${run.CollectorTake:N0} - "
                    + "and the further over the quota you are, the more tokens you earn for the SHOP. Come up short and it's lights out.",
                    Left + 20f, Top + 495f, Full - 40f, 170f, 30f, Dim);

                bool made = run.TeamCash >= run.Quota;
                if (run.Authority)
                {
                    surface.Button(made ? "PAY UP NOW  -  SKIP TO TOMORROW" : "NOT ENOUGH TO PAY UP YET", Left, Top + 690f, Full, 120f, Green, run.PayUpNow, made);
                    surface.Button("GIVE UP  -  GET SHOT", Left, Top + 835f, Full, 84f, Red, run.GiveUp, true, 32f);
                }
                else surface.Text("The host decides when to pay up.", Left, Top + 700f, Full, 80f, 34f, Dim);
                break;
            }
            case RunPhase.Gathering:
            {
                surface.Box(Left, Top, Full, 470f, Card);
                surface.Text("AT THE DOOR", Left, Top + 30f, Full, 100f, 80f, Gold, TextAlignmentOptions.Center, true);
                Live(surface.Text("", Left, Top + 150f, Full, 80f, 56f, Color.white, TextAlignmentOptions.Center, true),
                    () => run.ReadyCount + " / " + run.Players + " READY");
                surface.Text($"Day {run.Day} quota: ${run.Quota:N0}\n\nSpend your tokens at the Token Stall, then everyone presses the casino door "
                    + "(point at it and squeeze grip). When all of you have, the doors open and the clock starts.",
                    Left + 30f, Top + 250f, Full - 60f, 200f, 30f, Dim);
                if (run.Authority && run.Day <= 1)
                    surface.Button("CANCEL - BACK TO THE HUB", Left, Top + 510f, Full, 96f, Grey, run.Abandon, true, 34f);
                if (run.Authority)
                    surface.Button("GIVE UP  -  GET SHOT", Left, Top + 630f, Full, 96f, Red, run.GiveUp, true, 34f);
                break;
            }
            case RunPhase.Dead:
            {
                int days = Mathf.Max(0, run.DiedOnDay - 1);
                surface.Box(Left, Top, Full, 420f, Card);
                surface.Text("YOU GOT WHACKED", Left, Top + 50f, Full, 110f, 84f, BadText, TextAlignmentOptions.Center, true);
                surface.Text($"The quota on day {run.DiedOnDay} was too much.\nYou lasted {days} day{(days == 1 ? "" : "s")}.   Best: {run.BestDay}",
                    Left, Top + 190f, Full, 160f, 40f, Color.white);
                if (run.Authority)
                {
                    surface.Button("TRY AGAIN", Left, Top + 460f, Full, 130f, Green, run.StartRun, true, 48f);
                    surface.Button("BACK TO THE HUB", Left, Top + 615f, Full, 90f, Grey, run.Abandon);
                }
                else surface.Text("Waiting for the host to start again...", Left, Top + 470f, Full, 80f, 36f, Dim);
                break;
            }
            default:
            {
                surface.Box(Left, Top, Full, 560f, Card);
                surface.Text("THE HUB", Left, Top + 30f, Full, 100f, 80f, Gold, TextAlignmentOptions.Center, true);
                int quota = run.QuotaFor(1, run.Players);
                surface.Text("This is play money - gamble all you like while your friends join (FRIENDS tab). "
                    + "Starting a game swaps it for real debt.\n\n"
                    + $"Every day you must end with the quota in cash - ${quota:N0} on day 1, more each day. "
                    + "Make it and the collector takes his cut. Miss it and you get shot.\n\n"
                    + "Friends share one quota and one pool of cash.",
                    Left + 30f, Top + 140f, Full - 60f, 340f, 32f, Color.white);
                Live(surface.Text("", Left, Top + 490f, Full, 50f, 30f, Dim),
                    () => run.BestDay > 0 ? "Best run: " + run.BestDay + " day" + (run.BestDay == 1 ? "" : "s") : "");
                if (run.Authority)
                    surface.Button(run.Players > 1 ? "START GAME  (" + run.Players + " PLAYERS)" : "START GAME", Left, Top + 600f, Full, 140f, Green, run.StartRun, true, 50f);
                else surface.Text("Waiting for the host to start the run...", Left, Top + 620f, Full, 80f, 36f, Dim);
                break;
            }
        }
    }

    void ShopPage()
    {
        for (int i = 0; i < 4; i++)
        {
            var powerup = (Powerup)i;
            float y = Top + i * 238f;
            surface.Box(Left, y, Full, 222f, Card);
            surface.Text(CasinoRun.Title(powerup), Left + 30f, y + 16f, 620f, 56f, 40f, Gold, TextAlignmentOptions.Left, true);
            surface.Text(run.Describe(powerup), Left + 30f, y + 74f, 600f, 100f, 27f, Dim, TextAlignmentOptions.TopLeft);
            Live(surface.Text("", Left + 30f, y + 174f, 600f, 40f, 26f, GoodText, TextAlignmentOptions.Left), () => run.Owned(powerup));

            string blocked = run.WhyNot(powerup);
            int price = run.Price(powerup);
            string cost = price + (price == 1 ? " TOKEN" : " TOKENS");
            // With a Token Stall in the casino this page is only a price list.
            if (TokenStall.Instance != null)
            {
                surface.Text(cost, Left + 660f, y + 50f, Full - 690f, 60f, 38f, Gold, TextAlignmentOptions.Center, true);
                surface.Text("AT THE TOKEN STALL\nOUTSIDE THE DOOR", Left + 660f, y + 112f, Full - 690f, 70f, 22f, Dim);
                continue;
            }
            surface.Button(blocked ?? "BUY\n<size=70%>" + cost + "</size>", Left + 660f, y + 46f, Full - 690f, 130f, Green,
                () => run.Buy(powerup), blocked == null, blocked == null ? 36f : 26f);
            if (blocked != null && !blocked.StartsWith("NEED"))
                surface.Text(cost, Left + 660f, y + 10f, Full - 690f, 36f, 26f, Dim);
        }
    }

    void FriendsPage()
    {
        var mp = Multiplayer;
        if (mp == null || !mp.Ready)
        {
            surface.Text(mp != null ? mp.Status : "Multiplayer is not available in this scene.", Left, Top + 40f, Full, 200f, 36f, Dim);
            return;
        }

        surface.Box(Left, Top, Full, 190f, Card);
        Live(surface.Text("", Left + 20f, Top + 14f, Full - 40f, 110f, 34f, Color.white), () => mp.Status);
        surface.Text(string.IsNullOrEmpty(mp.LocalAddresses) ? "" : "Your IP: " + mp.LocalAddresses, Left + 20f, Top + 130f, Full - 40f, 46f, 28f, Dim);

        if (mp.InSession || mp.Joining)
        {
            Live(surface.Text("", Left, Top + 230f, Full, 80f, 44f, Gold, TextAlignmentOptions.Center, true), () =>
                mp.Joining ? "Connecting..." : mp.PlayerCount + " / " + CasinoMultiplayer.MaxPlayers + " players in the casino");
            if (mp.IsHost)
                surface.Text("Friends on the same Wi-Fi will see your game in their FRIENDS tab.", Left + 20f, Top + 320f, Full - 40f, 100f, 30f, Dim);
            surface.Button(mp.Joining ? "CANCEL" : mp.IsHost ? "STOP HOSTING" : "LEAVE GAME", Left, Top + 460f, Full, 120f, Red, mp.Leave);
            return;
        }

        surface.Button("HOST A GAME", Left, Top + 215f, Full, 130f, Blue, mp.Host, !mp.Busy, 46f);
        surface.Text("GAMES ON YOUR WI-FI", Left, Top + 375f, Full, 50f, 30f, Gold, TextAlignmentOptions.Left, true);

        var found = mp.Discovery != null ? mp.Discovery.Hosts : null;
        int shown = 0;
        if (found != null)
            for (int i = 0; i < found.Count && shown < 3; i++)
            {
                var host = found[i];
                bool full = host.Players >= CasinoMultiplayer.MaxPlayers;
                surface.Button("JOIN  " + host.Address + "   (" + host.Players + "/" + CasinoMultiplayer.MaxPlayers + ")",
                    Left, Top + 435f + shown * 122f, Full, 108f, Green, () => mp.Join(host.Address), !full && !mp.Busy, 36f);
                shown++;
            }
        if (shown == 0)
            surface.Text("Looking for games... nobody nearby is hosting yet.", Left, Top + 450f, Full, 80f, 30f, Dim);

        surface.Button("JOIN BY IP ADDRESS", Left, Top + 830f, Full, 96f, Grey, () =>
        {
            keypad = true;
            if (address.Length == 0) address = AddressPrefix(mp.LocalAddresses);
        }, true, 34f);
    }

    void KeypadPage()
    {
        var mp = Multiplayer;
        surface.Box(Left, Top, Full, 150f, Card);
        surface.Text("HOST'S IP ADDRESS", Left, Top + 10f, Full, 40f, 26f, Dim);
        var shown = surface.Text("", Left, Top + 50f, Full, 90f, 64f, Color.white, TextAlignmentOptions.Center, true);
        Live(shown, () => address + "_");

        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
        float width = (Full - 40f) / 3f;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            surface.Button(key, Left + (i % 3) * (width + 20f), Top + 175f + (i / 3) * 128f, width, 112f, Grey, () =>
            {
                if (key == "DEL") address = address.Length > 0 ? address.Substring(0, address.Length - 1) : "";
                else if (address.Length < 15) address += key;
                shown.text = address + "_";
                surface.MarkDirty();
            }, true, 50f);
        }

        surface.Button("JOIN", Left, Top + 700f, Full * 0.62f, 120f, Green, () =>
        {
            if (mp == null || !mp.Ready) return;
            mp.Join(address);
            keypad = false;
        }, mp != null && mp.Ready && !mp.Busy, 46f);
        surface.Button("BACK", Left + Full * 0.62f + 20f, Top + 700f, Full * 0.38f - 20f, 120f, Grey, () => keypad = false);
    }

    // "192.168.1.20 / 10.0.0.5" -> "192.168.1." so only the last number needs typing.
    static string AddressPrefix(string localAddresses)
    {
        if (string.IsNullOrEmpty(localAddresses)) return "";
        string first = localAddresses.Split('/')[0].Trim();
        int dot = first.LastIndexOf('.');
        return dot > 0 ? first.Substring(0, dot + 1) : "";
    }

    // Text that keeps itself up to date while the page is open.
    void Live(TextMeshProUGUI text, Func<string> value)
    {
        live.Add(() =>
        {
            string now = value();
            if (text == null || text.text == now) return false;
            text.text = now;
            return true;
        });
    }
}
