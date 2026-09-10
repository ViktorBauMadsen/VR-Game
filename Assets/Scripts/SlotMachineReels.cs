using System.Collections;
using UnityEngine;

// Simulates a spinning slot reel using 4 planes that scroll within the
// vertical bounds of a reference "screen" object (assign screenBounds to
// that marker object - its Renderer bounds define the top/bottom of the
// travel range, in world space). While spinning, each plane slides
// continuously from the top of the screen down to the bottom, briefly
// hiding to jump back to the top when it reaches the bottom (so the loop
// doesn't visibly teleport). Planes land in a left-to-right cascade (like a
// real machine) - each one stops landStagger seconds after the one to its
// left - sliding smoothly back to its own home position and settling there
// with the real result color.
public class SlotMachineReels : MonoBehaviour
{
    [SerializeField] Transform[] planes = new Transform[4];
    [SerializeField] Renderer screenBounds; // defines the top/bottom of the travel range
    [SerializeField] float scrollSpeed = 0.3f; // units/sec while spinning
    [SerializeField] float settleSpeed = 1f;   // units/sec when sliding back home at the end
    [SerializeField] float wrapHideDuration = 0.05f; // brief hide when jumping from bottom back to top
    [SerializeField] float landStagger = 0.1f; // extra seconds each plane waits, left to right

    Renderer[] _renderers;
    Vector3[] _homePositions; // world space
    float[] _hiddenTimer;
    int[] _leftToRightOrder;
    float _topY, _bottomY; // world space

    Sprite[] _symbolPool;
    Sprite[] _pendingResults; // one per plane

    // How much longer the last (rightmost) plane takes to settle, beyond
    // the base spin duration - SlotMachine can add this to its own payout
    // wait so the money updates in sync with the last reel landing.
    public float ExtraSettleDelay => Mathf.Max(0, planes.Length - 1) * landStagger;

    void Awake()
    {
        _renderers = new Renderer[planes.Length];
        _homePositions = new Vector3[planes.Length];
        _hiddenTimer = new float[planes.Length];

        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i] == null) continue;
            _renderers[i] = planes[i].GetComponent<Renderer>();
            _homePositions[i] = planes[i].position; // world space, avoids any local/parent-rotation conversion
        }

        // Use the order the planes were assigned in the Inspector directly
        // (Slot1 = index 0 lands first, etc.) rather than guessing which
        // world axis is "left" - much more reliable than inferring it.
        _leftToRightOrder = new int[planes.Length];
        for (int i = 0; i < planes.Length; i++) _leftToRightOrder[i] = i;

        if (screenBounds != null)
        {
            var b = screenBounds.bounds; // already world space
            _topY = b.max.y;
            _bottomY = b.min.y;
        }
        else
        {
            Debug.LogWarning("[SlotMachineReels] screenBounds not assigned - falling back to a small default travel range.");
            float homeY = _homePositions.Length > 0 ? _homePositions[0].y : 0f;
            _topY = homeY + 0.1f;
            _bottomY = homeY - 0.1f;
        }
    }

    public void SetSymbolPool(Sprite[] sprites)
    {
        _symbolPool = sprites;
        for (int i = 0; i < planes.Length; i++)
            SetSymbol(i, RandomSymbol());
    }

    public void PlaySpin(PayoutTier winningTier, float duration)
    {
        StopAllCoroutines();
        _pendingResults = winningTier != null ? WinningResults(winningTier) : LosingResults();

        for (int rank = 0; rank < _leftToRightOrder.Length; rank++)
        {
            int planeIndex = _leftToRightOrder[rank];
            float thisDuration = duration + rank * landStagger;
            StartCoroutine(PlaneSpinRoutine(planeIndex, thisDuration));
        }
    }

    IEnumerator PlaneSpinRoutine(int i, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            ScrollStep(i, Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Slide cleanly back home and settle.
        if (_renderers[i] != null) _renderers[i].enabled = true;
        while (planes[i] != null && planes[i].position != _homePositions[i])
        {
            planes[i].position = Vector3.MoveTowards(planes[i].position, _homePositions[i], settleSpeed * Time.deltaTime);
            yield return null;
        }
        SetSymbol(i, i < _pendingResults.Length ? _pendingResults[i] : null);
    }

    void ScrollStep(int i, float deltaTime)
    {
        if (planes[i] == null) return;

        if (_hiddenTimer[i] > 0f)
        {
            _hiddenTimer[i] -= deltaTime;
            if (_hiddenTimer[i] <= 0f && _renderers[i] != null)
                _renderers[i].enabled = true;
            return;
        }

        var pos = planes[i].position;
        pos.y -= scrollSpeed * deltaTime;

        if (pos.y < _bottomY)
        {
            pos.y = _topY;
            if (_renderers[i] != null) _renderers[i].enabled = false;
            _hiddenTimer[i] = wrapHideDuration;
            SetSymbol(i, RandomSymbol());
        }

        planes[i].position = pos;
    }

    void SetSymbol(int index, Sprite sprite)
    {
        if (index < 0 || index >= _renderers.Length || _renderers[index] == null || sprite == null) return;

        var mat = _renderers[index].material;
        var tex = sprite.texture;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        mat.color = Color.white; // let the texture show through undistorted
    }

    Sprite RandomSymbol()
    {
        if (_symbolPool == null || _symbolPool.Length == 0) return null;
        return _symbolPool[Random.Range(0, _symbolPool.Length)];
    }

    Sprite[] WinningResults(PayoutTier tier)
    {
        var result = new Sprite[planes.Length];
        for (int i = 0; i < result.Length; i++) result[i] = tier.symbol;
        return result;
    }

    // Independent random symbol per plane, rerolled if they'd accidentally
    // all match (which would misleadingly look like a win).
    Sprite[] LosingResults()
    {
        var result = new Sprite[planes.Length];
        int guard = 0;
        bool allMatch;
        do
        {
            for (int i = 0; i < result.Length; i++) result[i] = RandomSymbol();
            allMatch = true;
            for (int i = 1; i < result.Length; i++)
                if (result[i] != result[0]) { allMatch = false; break; }
            guard++;
        } while (allMatch && guard < 20);
        return result;
    }
}
