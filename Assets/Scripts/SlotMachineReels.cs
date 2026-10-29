using System;
using System.Collections;
using UnityEngine;

// Simulates spinning slot reels using columns of 3 fixed planes each (top,
// middle, bottom - only the texture shown on each changes). Every tick
// during a spin the symbols shift down the column (bottom <- middle <- top,
// top gets a new random symbol), so symbols visibly flow downward.
//
// Only the middle slot is the payline. Reels stop one at a time, left to
// right: the first after spinTime, then each next one reelStopGap seconds
// later ("1... 2... 3... 4"). Just before stopping, a reel's ticks slow
// down, and on the final tick the middle slot is forced to the real result
// and the column does a small bounce.
public class SlotMachineReels : MonoBehaviour
{
    [Serializable]
    public class ReelColumn
    {
        public Transform top;
        public Transform middle;
        public Transform bottom;
    }

    class ColumnState
    {
        public Renderer top, middle, bottom;
        public Transform topT, middleT, bottomT;
        public Vector3 topBase, middleBase, bottomBase;
        public Sprite topSprite, middleSprite, bottomSprite;
        public Coroutine bounce;
    }

    [SerializeField] ReelColumn[] columns = new ReelColumn[4];

    [Header("Timing")]
    [Tooltip("Seconds of spinning before the FIRST reel stops.")]
    [SerializeField] float spinTime = 2.5f;
    [Tooltip("Seconds between each reel stopping, left to right.")]
    [SerializeField] float reelStopGap = 0.7f;
    [Tooltip("Seconds between symbol shifts at full speed (smaller = faster).")]
    [SerializeField] float fastTickInterval = 0.04f;
    [Tooltip("Seconds between symbol shifts right before a reel stops.")]
    [SerializeField] float slowTickInterval = 0.12f;
    [Tooltip("How long before its stop a reel starts slowing down.")]
    [SerializeField] float slowDownTime = 0.45f;

    [Header("Stop bounce")]
    [Tooltip("How far the column dips when a reel stops, as a fraction of the gap between two symbols. 0 = no bounce.")]
    [SerializeField, Range(0f, 0.5f)] float stopBounce = 0.12f;
    [SerializeField] float stopBounceTime = 0.18f;

    ColumnState[] _columns;
    Sprite[] _symbolPool;
    Sprite[] _pendingResults; // one per column
    int _reelsSpinning;

    // For audio: a reel shifted one symbol / a reel came to a stop (index
    // 0 = leftmost) / every reel has stopped.
    public event Action<int> ReelTicked;
    public event Action<int> ReelStopped;
    public event Action AllStopped;

    public bool IsSpinning => _reelsSpinning > 0;
    public int ReelCount => columns.Length;
    // Total time from starting a spin until the last reel stops.
    public float TotalSpinTime => spinTime + Mathf.Max(0, columns.Length - 1) * reelStopGap;

    void Awake()
    {
        _columns = new ColumnState[columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            var src = columns[c];
            var col = new ColumnState { topT = src.top, middleT = src.middle, bottomT = src.bottom };
            col.top = src.top != null ? src.top.GetComponent<Renderer>() : null;
            col.middle = src.middle != null ? src.middle.GetComponent<Renderer>() : null;
            col.bottom = src.bottom != null ? src.bottom.GetComponent<Renderer>() : null;
            if (col.topT != null) col.topBase = col.topT.localPosition;
            if (col.middleT != null) col.middleBase = col.middleT.localPosition;
            if (col.bottomT != null) col.bottomBase = col.bottomT.localPosition;
            _columns[c] = col;
        }
    }

    // Where the reel "screen" is in the world, worked out from the reel
    // planes: its center, a rotation facing the player (forward points INTO
    // the screen, up is screen-up), the gap between two rows, and its width.
    public bool TryGetScreenFrame(out Vector3 center, out Quaternion facing, out float rowSpacing, out float width)
    {
        center = Vector3.zero; facing = Quaternion.identity; rowSpacing = 0f; width = 0f;
        Transform first = null, last = null;
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var col in columns)
        {
            if (col == null || col.middle == null) continue;
            if (first == null) first = col.middle;
            last = col.middle;
            sum += col.middle.position;
            n++;
        }
        if (n == 0 || columns[0].bottom == null) return false;

        center = sum / n;
        Vector3 normal = first.up; // Unity Plane meshes face their local +Y
        Vector3 down = columns[0].bottom.position - columns[0].middle.position;
        rowSpacing = down.magnitude;
        Vector3 up = Vector3.ProjectOnPlane(-down, normal).normalized;
        if (up.sqrMagnitude < 1e-6f) return false;
        facing = Quaternion.LookRotation(-normal, up);

        float span = Vector3.Distance(first.position, last.position);
        width = n > 1 ? span * n / (n - 1) : rowSpacing;
        return true;
    }

    public void SetSymbolPool(Sprite[] sprites)
    {
        _symbolPool = sprites;
        foreach (var col in _columns)
        {
            col.topSprite = RandomSymbol();
            col.middleSprite = RandomSymbol();
            col.bottomSprite = RandomSymbol();
            ApplySprite(col.top, col.topSprite);
            ApplySprite(col.middle, col.middleSprite);
            ApplySprite(col.bottom, col.bottomSprite);
        }
    }

    public void PlaySpin(PayoutTier winningTier)
    {
        StopAllCoroutines();
        foreach (var col in _columns) ResetBounce(col);

        _pendingResults = winningTier != null ? WinningResults(winningTier) : LosingResults();
        _reelsSpinning = _columns.Length;

        for (int c = 0; c < _columns.Length; c++)
            StartCoroutine(ColumnSpinRoutine(c, spinTime + c * reelStopGap));
    }

    IEnumerator ColumnSpinRoutine(int c, float stopTime)
    {
        var col = _columns[c];

        float elapsed = 0f;
        while (elapsed < stopTime)
        {
            ShiftDown(col, RandomSymbol());
            ReelTicked?.Invoke(c);

            // Full speed, then ease down to slowTickInterval near the stop.
            float remaining = stopTime - elapsed;
            float interval = remaining > slowDownTime
                ? fastTickInterval
                : Mathf.Lerp(slowTickInterval, fastTickInterval, remaining / Mathf.Max(0.01f, slowDownTime));
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        // Final shift: force the real result into the middle (payline) slot.
        ShiftDown(col, RandomSymbol());
        col.middleSprite = _pendingResults[c];
        ApplySprite(col.middle, col.middleSprite);

        if (stopBounce > 0f) col.bounce = StartCoroutine(BounceRoutine(col));

        _reelsSpinning--;
        ReelStopped?.Invoke(c);
        if (_reelsSpinning == 0) AllStopped?.Invoke();
    }

    // The column dips a little in the direction symbols flow, then springs back.
    IEnumerator BounceRoutine(ColumnState col)
    {
        if (col.middleT == null || col.bottomT == null || col.middleT.parent != col.bottomT.parent) yield break;

        Vector3 flowDir = col.bottomBase - col.middleBase; // one symbol step, in parent space
        float t = 0f;
        while (t < stopBounceTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / stopBounceTime);
            float dip = Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f);
            SetOffset(col, flowDir * (stopBounce * dip));
            yield return null;
        }
        SetOffset(col, Vector3.zero);
        col.bounce = null;
    }

    void ResetBounce(ColumnState col)
    {
        col.bounce = null; // coroutine already stopped by StopAllCoroutines
        SetOffset(col, Vector3.zero);
    }

    static void SetOffset(ColumnState col, Vector3 offset)
    {
        if (col.topT != null) col.topT.localPosition = col.topBase + offset;
        if (col.middleT != null) col.middleT.localPosition = col.middleBase + offset;
        if (col.bottomT != null) col.bottomT.localPosition = col.bottomBase + offset;
    }

    void ShiftDown(ColumnState col, Sprite newTop)
    {
        col.bottomSprite = col.middleSprite;
        col.middleSprite = col.topSprite;
        col.topSprite = newTop;

        ApplySprite(col.top, col.topSprite);
        ApplySprite(col.middle, col.middleSprite);
        ApplySprite(col.bottom, col.bottomSprite);
    }

    void ApplySprite(Renderer renderer, Sprite sprite)
    {
        if (renderer == null || sprite == null) return;
        var mat = renderer.material;
        var tex = sprite.texture;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        mat.color = Color.white;
    }

    Sprite RandomSymbol()
    {
        if (_symbolPool == null || _symbolPool.Length == 0) return null;
        return _symbolPool[UnityEngine.Random.Range(0, _symbolPool.Length)];
    }

    Sprite[] WinningResults(PayoutTier tier)
    {
        var result = new Sprite[columns.Length];
        for (int i = 0; i < result.Length; i++) result[i] = tier.symbol;
        return result;
    }

    Sprite[] LosingResults()
    {
        var result = new Sprite[columns.Length];
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
