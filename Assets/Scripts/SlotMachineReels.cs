using System.Collections;
using UnityEngine;

// Simulates spinning slot reels using 4 columns of 3 fixed planes each
// (top, middle, bottom - they never move, only the texture shown on each
// changes). Every tick during a spin, the symbols shift down the column:
// bottom takes whatever middle was showing, middle takes whatever top was
// showing, and top gets a brand new random symbol - so a symbol visibly
// "flows" from top to middle to bottom over successive ticks, then exits,
// always introducing fresh variety rather than symbols looping in place.
// Only the middle slot is the actual "payline" - when a column's spin
// ends, one final shift happens and the middle slot is forced to show the
// real result. Columns land in a left-to-right cascade: each column stops
// landStagger seconds after the column to its left.
public class SlotMachineReels : MonoBehaviour
{
    [System.Serializable]
    public class ReelColumn
    {
        public Transform top;
        public Transform middle;
        public Transform bottom;
    }

    class ColumnState
    {
        public Renderer top, middle, bottom;
        public Sprite topSprite, middleSprite, bottomSprite;
    }

    [SerializeField] ReelColumn[] columns = new ReelColumn[4];
    [SerializeField] float tickInterval = 0.15f; // how often symbols shift down during a spin
    [SerializeField] float landStagger = 0.1f;   // extra seconds each column waits, left to right

    ColumnState[] _columns;
    Sprite[] _symbolPool;
    Sprite[] _pendingResults; // one per column

    public float ExtraSettleDelay => Mathf.Max(0, columns.Length - 1) * landStagger;

    void Awake()
    {
        _columns = new ColumnState[columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            var src = columns[c];
            _columns[c] = new ColumnState
            {
                top = src.top != null ? src.top.GetComponent<Renderer>() : null,
                middle = src.middle != null ? src.middle.GetComponent<Renderer>() : null,
                bottom = src.bottom != null ? src.bottom.GetComponent<Renderer>() : null,
            };
        }
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

    public void PlaySpin(PayoutTier winningTier, float duration)
    {
        StopAllCoroutines();
        _pendingResults = winningTier != null ? WinningResults(winningTier) : LosingResults();

        for (int c = 0; c < _columns.Length; c++)
        {
            float thisDuration = duration + c * landStagger;
            StartCoroutine(ColumnSpinRoutine(c, thisDuration));
        }
    }

    IEnumerator ColumnSpinRoutine(int c, float duration)
    {
        var col = _columns[c];

        float elapsed = 0f;
        while (elapsed < duration)
        {
            ShiftDown(col, RandomSymbol());
            yield return new WaitForSeconds(tickInterval);
            elapsed += tickInterval;
        }

        // Final shift: force the real result into the middle (payline) slot.
        ShiftDown(col, RandomSymbol());
        col.middleSprite = _pendingResults[c];
        ApplySprite(col.middle, col.middleSprite);
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
        return _symbolPool[Random.Range(0, _symbolPool.Length)];
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
