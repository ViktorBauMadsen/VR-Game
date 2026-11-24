using System.Collections.Generic;
using UnityEngine;

// What the strolling guests talk about. Each guest gets a little story for
// the night when they spawn - won or lost, on which game, how much - and the
// lines are built from it, so what they tell the player and what they tell
// each other stays consistent.
public class PatronDialogue
{
    static readonly string[] Slots = { "Golden Inferno", "Fruit Cocktail", "Star Wheel" };
    static readonly string[] Tables = { "Blackjack", "Roulette" };
    static readonly string[] Amounts = { "$500", "$1,200", "$3,000", "$8,000", "$20,000" };

    public readonly bool Won;
    public readonly string Game, Amount;
    readonly bool _onSlots;
    readonly string _otherGame;

    public PatronDialogue()
    {
        Won = Random.value < 0.45f;
        _onSlots = Random.value < 0.55f;
        Game = _onSlots ? Pick(Slots) : Pick(Tables);
        Amount = Pick(Amounts);
        do _otherGame = Random.value < 0.5f ? Pick(Slots) : Pick(Tables);
        while (_otherGame == Game);
    }

    static string Pick(string[] options) => options[Random.Range(0, options.Length)];

    // ------------------------------------------------------------ about me

    // "How did tonight go", in their own words.
    public string Brag()
    {
        if (Won)
            return _onSlots && Random.value < 0.6f
                ? Pick(new[] { $"I hit the jackpot on {Game}!", $"Three in a row on {Game}. Jackpot!" })
                : Pick(new[] { $"I'm up {Amount} on {Game} tonight!", $"{Game} has been very kind to me tonight.", $"Won {Amount} at {Game}. Don't tell my wife." });
        return Pick(new[] { $"I lost all my money on {Game}.", $"{Game} took everything I had.", $"I'm down {Amount}. {Game} hates me.", $"Don't ask. {Game} cleaned me out." });
    }

    public string Tip() => Won
        ? Pick(new[] { $"Try {Game}. It's been paying all night.", $"If I were you, I'd play {Game}.", $"{Game} is hot tonight. Trust me." })
        : Pick(new[] { $"Stay away from {Game}. Trust me.", $"Whatever you do, don't play {Game}.", $"Try {_otherGame}. Anything but {Game}." });

    static string Gossip() => Pick(new[]
    {
        "Have you seen the man in the tuxedo at the bar? I think he's a spy.",
        "The bartender makes a mean martini.",
        "I heard there's an alien selling lucky charms around here.",
        "The roulette croupier never blinks. Ever.",
        "Someone said the dealer at table three counts cards. Backwards.",
        "Lovely place, isn't it? Look at those chandeliers!",
    });

    // What they say to the player, one line per press of the button.
    public List<string> ToPlayer()
    {
        var lines = new List<string>
        {
            Pick(new[] { "Oh, hello there!", "Good evening!", "Hi! Enjoying the casino?", "Well, hello!" }),
            Brag(),
            Tip(),
            Gossip(),
            Pick(new[] { "Well, back to the tables. Good luck!", "Nice chatting! Good luck out there.", "Don't spend it all in one place!" }),
        };
        return lines;
    }

    // ------------------------------------------------------------ two guests

    // A short exchange between two guests. Each entry: (does a speak?, line).
    public static List<(bool a, string line)> Chat(PatronDialogue a, PatronDialogue b)
    {
        var lines = new List<(bool, string)>();
        float roll = Random.value;
        if (roll < 0.5f)
        {
            lines.Add((true, Pick(new[] { "Any luck today?", "How's the night treating you?", "Winning anything?" })));
            lines.Add((false, (b.Won ? Pick(new[] { "Yes! ", "Actually, yes! " }) : Pick(new[] { "No. ", "Not really. " })) + b.Brag()));
            lines.Add((true, Reaction(b)));
            if (Random.value < 0.5f)
            {
                lines.Add((false, Pick(new[] { "And you?", "How about you?" })));
                lines.Add((true, (a.Won ? "Yes! " : Pick(new[] { "No. ", "Not great. " })) + a.Brag()));
                lines.Add((false, Reaction(a)));
            }
        }
        else if (roll < 0.7f)
        {
            lines.Add((true, Pick(new[] { "Which game should I try next?", "Any tips? I'm new here.", "What's paying tonight?" })));
            lines.Add((false, b.Tip()));
            lines.Add((true, Pick(new[] { "Thanks, I'll keep that in mind.", "Good to know!", "I'll give it a go." })));
        }
        else if (roll < 0.85f)
        {
            lines.Add((true, Pick(new[] { "Have you seen the man in the tuxedo at the bar?", "Who's the fellow drinking martinis at the bar?" })));
            lines.Add((false, Pick(new[] { "Shaken, not stirred, apparently. Very mysterious.", "I heard he never loses at cards.", "He introduced himself twice. Same name both times." })));
            lines.Add((true, Pick(new[] { "Something about him seems... dangerous.", "I think he's a spy.", "Best not to stare." })));
        }
        else
        {
            lines.Add((true, Pick(new[] { "Lovely evening, isn't it?", "Have you tried the bar? The martinis are excellent." })));
            lines.Add((false, Pick(new[] { "Wonderful. I could stay all night.", "It is! I love the chandeliers.", "I'm saving myself for the next round." })));
        }
        return lines;
    }

    static string Reaction(PatronDialogue other) => other.Won
        ? Pick(new[] { "Congratulations! Drinks are on you then.", $"Lucky you! Maybe I'll try {other.Game}.", "Nice! Don't spend it all at once." })
        : Pick(new[] { "Ouch. Better luck tomorrow.", "Tough night. The bar might help.", $"That's rough. Maybe try {other._otherGame}?" });
}
