using System.Collections;
using UnityEngine;

// Hold the cigar up to your mouth to take a drag: the ember glows bright
// while it's there, and when you take it away again you breathe out a cloud
// of smoke. After the first drag it stays lit, smouldering with a thin wisp
// of smoke from the tip. It's finished after a number of drags.
public class Cigar : BarConsumable
{
    [SerializeField] Renderer ember;
    [SerializeField] ParticleSystem tipSmoke;
    [SerializeField] ParticleSystem exhaleSmoke;
    [SerializeField] int drags = 12;
    [SerializeField] float minDragSeconds = 0.35f;
    [SerializeField] Color ashColor = new Color(0.35f, 0.33f, 0.32f);
    [SerializeField] Color emberColor = new Color(1f, 0.35f, 0.05f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    Material _emberMat;
    bool _lit;
    float _glow;
    float _dragTime;
    int _dragsLeft;

    protected override void Awake()
    {
        base.Awake();
        _dragsLeft = drags;
        if (ember != null)
        {
            // Material instance, not a MaterialPropertyBlock (GPU Resident Drawer).
            _emberMat = ember.material;
            _emberMat.EnableKeyword("_EMISSION");
        }
        ApplyGlow();
    }

    void OnDestroy()
    {
        if (_emberMat != null) Destroy(_emberMat);
    }

    protected override void Update()
    {
        base.Update();

        bool dragging = IsHeld && AtMouth && _dragsLeft > 0;
        if (dragging)
        {
            if (_dragTime == 0f) Haptic(0.25f, 0.1f);
            _dragTime += Time.deltaTime;
            if (_dragTime > 0.2f) _lit = true;
        }
        else if (_dragTime > 0f)
        {
            if (_dragTime >= minDragSeconds) StartCoroutine(Exhale(Mathf.Clamp(_dragTime, 0.6f, 2.5f)));
            _dragTime = 0f;
        }

        float smoulder = 0.22f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, 0f);
        float target = dragging ? 1f : _lit ? smoulder : 0f;
        _glow = Mathf.MoveTowards(_glow, target, Time.deltaTime * (dragging ? 2.5f : 0.7f));
        ApplyGlow();

        if (tipSmoke != null)
        {
            var emission = tipSmoke.emission;
            emission.rateOverTime = _lit ? Mathf.Lerp(5f, 16f, _glow) : 0f;
        }
    }

    void ApplyGlow()
    {
        if (_emberMat == null) return;
        _emberMat.SetColor(BaseColorId, Color.Lerp(ashColor, emberColor, _glow));
        _emberMat.SetColor(EmissionColorId, emberColor * (_glow * 5f));
    }

    // Smoke is emitted in world space at the mouth for a moment, following
    // the head, so it drifts away in front of the player's face.
    IEnumerator Exhale(float strength)
    {
        _dragsLeft--;
        if (_dragsLeft <= 0)
        {
            _lit = false;
            MarkSpent();
        }

        yield return new WaitForSeconds(0.25f);
        if (Head != null) BarSounds.Play(BarSounds.Exhale, MouthPosition, 0.35f);

        float duration = 0.5f + strength * 0.4f;
        float carry = 0f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (exhaleSmoke == null || Head == null) yield break;

            carry += 45f * Time.deltaTime;
            int count = (int)carry;
            carry -= count;
            for (int i = 0; i < count; i++)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = MouthPosition + Head.forward * 0.08f + Random.insideUnitSphere * 0.015f,
                    velocity = (Head.forward - Head.up * 0.15f) * Random.Range(0.35f, 0.7f) + Random.insideUnitSphere * 0.08f,
                };
                exhaleSmoke.Emit(p, 1);
            }
            yield return null;
        }
    }
}
