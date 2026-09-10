using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Attach to the slot machine's screen canvas, with 3 Image slots assigned
// (the visible row). Call PlaySpin to animate a quick symbol flicker and
// settle on a predetermined outcome: a winning tier shows 3 matching
// symbols, a loss (tier == null) shows 3 non-matching symbols.
//
// The canvas renders via a dedicated off-screen camera into a Render Texture
// that's applied directly to the slot machine's screen material (see
// Tools > Setup Slot Machine Screen Render Texture), so there's no separate
// plane to visually misalign with the mesh. The render camera is only kept
// enabled while spinning/settling - the Render Texture holds the last
// rendered frame in between, so the screen keeps showing the result at no
// ongoing render cost, which matters on standalone VR hardware.
public class SlotMachineReels : MonoBehaviour
{
    [SerializeField] Image[] reelSlots = new Image[3];
    [SerializeField] float spinFlickerInterval = 0.08f;
    [SerializeField] Camera reelCamera;

    Sprite[] _symbolPool;

    public void SetSymbolPool(Sprite[] symbols)
    {
        _symbolPool = symbols;

        // Show something on the screen before the first pull, rather than
        // leaving the Render Texture blank/black.
        foreach (var slot in reelSlots)
            if (slot != null) slot.sprite = RandomSymbol();
        StartCoroutine(RenderOneFrame());
    }

    IEnumerator RenderOneFrame()
    {
        if (reelCamera == null) yield break;
        reelCamera.enabled = true;
        // Give the Canvas a few frames to finish its first layout/rebuild
        // pass before capturing - a freshly-enabled Canvas isn't always
        // guaranteed to have valid renderable geometry on the very first frame.
        for (int i = 0; i < 5; i++) yield return null;
        reelCamera.enabled = false;
    }

    public void PlaySpin(PayoutTier winningTier, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(SpinRoutine(winningTier, duration));
    }

    IEnumerator SpinRoutine(PayoutTier winningTier, float duration)
    {
        if (reelCamera != null) reelCamera.enabled = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            foreach (var slot in reelSlots)
                if (slot != null) slot.sprite = RandomSymbol();

            yield return new WaitForSeconds(spinFlickerInterval);
            elapsed += spinFlickerInterval;
        }

        Sprite[] finalSymbols = winningTier != null
            ? new[] { winningTier.symbol, winningTier.symbol, winningTier.symbol }
            : RandomNonMatchingSymbols();

        for (int i = 0; i < reelSlots.Length && i < finalSymbols.Length; i++)
            if (reelSlots[i] != null) reelSlots[i].sprite = finalSymbols[i];

        // Let the camera render a few more frames with the final symbols
        // before switching it off, so the Render Texture reliably captures
        // the settled result rather than freezing on a mid-flicker frame.
        for (int i = 0; i < 5; i++) yield return null;
        if (reelCamera != null) reelCamera.enabled = false;
    }

    Sprite RandomSymbol()
    {
        if (_symbolPool == null || _symbolPool.Length == 0) return null;
        return _symbolPool[Random.Range(0, _symbolPool.Length)];
    }

    Sprite[] RandomNonMatchingSymbols()
    {
        var result = new Sprite[3];
        int guard = 0;
        do
        {
            for (int i = 0; i < 3; i++) result[i] = RandomSymbol();
            guard++;
        } while (result[0] == result[1] && result[1] == result[2] && guard < 20);
        return result;
    }
}
