using System.Collections.Generic;
using UnityEngine;

// Sets up the chip pockets at your waist as $5, $25, $100, $1,000 and
// $5,000, left to right.
//
// The chip prefabs are red $5, blue $10, green $25 and black $100. When the
// game starts this:
//   - turns the blue chip into the $100 chip and the black into the $1,000,
//   - adds a gold $5,000 chip with a fifth pocket,
//   - and lines the pockets up from cheapest to most expensive.
// The new chips are copies made in code (the gold one a recoloured copy of
// the black), handed out by the pockets instead of the original prefab.
// Nothing in the scene or the chip prefabs is changed, and it works in any
// scene that has chip pockets.
public static class HighRollerChip
{
    const int OldBlueValue = 10, NewBlueValue = 100, OldBlackValue = 100, NewBlackValue = 1000, GoldValue = 5000;
    const float PocketSpacing = 0.2f; // the pockets sit this far apart
    static readonly Color Gold = new Color(1f, 0.78f, 0.2f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
        Install();
    }

    static void SceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Install();

    static void Install()
    {
        var pockets = new List<ChipDispenser>();
        foreach (var pocket in Object.FindObjectsByType<ChipDispenser>(FindObjectsInactive.Include))
        {
            if (pocket.ChipPrefab == null) continue;
            if (pocket.ChipPrefab.Value >= GoldValue) return; // already set up
            pockets.Add(pocket);
        }
        if (pockets.Count == 0) return;

        // The places the pockets sit in, left to right, plus one more on the right.
        var slots = new List<float>();
        ChipDispenser best = pockets[0];
        foreach (var pocket in pockets)
        {
            slots.Add(pocket.transform.localPosition.x);
            if (pocket.ChipPrefab.Value > best.ChipPrefab.Value) best = pocket;
        }
        slots.Sort();
        slots.Add(slots[slots.Count - 1] + PocketSpacing);

        // Gold $5,000: a recoloured copy of the most valuable chip, in a new pocket.
        var goldChip = Template(best.ChipPrefab, "PokerChip_Gold", GoldValue);
        Tint(goldChip.gameObject);
        var goldPocket = Object.Instantiate(best.gameObject, best.transform.parent);
        goldPocket.name = "Pocket_Gold";
        goldPocket.GetComponent<ChipDispenser>().Configure(goldChip);
        Tint(goldPocket);
        pockets.Add(goldPocket.GetComponent<ChipDispenser>());

        // Blue: $10 becomes $100. Black: $100 becomes $1,000.
        foreach (var pocket in pockets)
        {
            int old = pocket.ChipPrefab.Value;
            if (old == OldBlueValue) pocket.Configure(Template(pocket.ChipPrefab, pocket.ChipPrefab.name + "_100", NewBlueValue));
            else if (old == OldBlackValue) pocket.Configure(Template(pocket.ChipPrefab, pocket.ChipPrefab.name + "_1000", NewBlackValue));
        }

        // Cheapest on the left, most expensive on the right.
        pockets.Sort((a, b) => a.ChipPrefab.Value.CompareTo(b.ChipPrefab.Value));
        for (int i = 0; i < pockets.Count && i < slots.Count; i++)
        {
            Vector3 position = pockets[i].transform.localPosition;
            position.x = slots[i];
            pockets[i].transform.localPosition = position;
        }
    }

    // A switched-off copy of a chip with a different value, for a pocket to
    // hand out copies of. (Kept out of the pocket anchor, which switches its
    // children on and off.)
    static CasinoChip Template(CasinoChip source, string name, int value)
    {
        var copy = Object.Instantiate(source.gameObject);
        copy.name = name;
        copy.SetActive(false);
        var chip = copy.GetComponent<CasinoChip>();
        chip.SetValue(value);
        return chip;
    }

    static void Tint(GameObject root)
    {
        Material gold = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterial == null) continue;
            if (gold == null)
            {
                gold = new Material(renderer.sharedMaterial) { name = "Gold Chip (runtime)", color = Gold };
                if (gold.HasProperty("_Metallic")) gold.SetFloat("_Metallic", 0.6f);
                if (gold.HasProperty("_Smoothness")) gold.SetFloat("_Smoothness", 0.75f);
            }
            renderer.sharedMaterial = gold;
        }
    }
}
