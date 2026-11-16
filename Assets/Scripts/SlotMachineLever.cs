using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Attach to the slot machine's lever handle, alongside an XR Simple Interactable
// (not a Grab Interactable - we don't want the handle snapping to the hand's
// full position/rotation, only reacting to how far down it's been pulled).
// Only vertical hand movement (measured in the stationary pivot's local space)
// affects the handle, so side-to-side or forward/back hand motion does nothing -
// it can only be pulled down, within a fixed angle range. Springs back to rest
// on release, and fires the slot machine once per pull when pulled far enough.
[RequireComponent(typeof(XRSimpleInteractable))]
public class SlotMachineLever : MonoBehaviour
{
    [SerializeField] Transform handle;      // the mesh that visually rotates; defaults to this object
    [SerializeField] Transform pivot;       // stationary reference frame for measuring hand movement; defaults to handle's parent
    [SerializeField] Vector3 hingeAxis = Vector3.right; // local axis (relative to pivot) the handle rotates around
    [SerializeField] bool invertRotation = false;
    [SerializeField] float maxPullAngle = 45f;      // degrees when fully pulled down
    [SerializeField] float maxPullDistance = 0.15f; // meters of downward hand movement for a full pull
    [SerializeField] float pullThreshold = 0.95f;   // fraction of maxPullAngle that counts as "fully pulled"
    [SerializeField] float returnSpeed = 180f;      // degrees/sec spring-back speed after release
    [SerializeField] SlotMachine slotMachine;
    [SerializeField] AudioSource audioSource;
    // Lever sounds are generated in code: ratchet clicks while you pull, and
    // a heavy "ka-chunk" when the pull is far enough to trigger a spin.
    // (The field was renamed from pullClip so every machine dropped the old
    // "Lever Pull.mp3". Assign a clip here only to override the ka-chunk.)
    [SerializeField] AudioClip customSpinClip;
    [SerializeField, Range(0f, 1f)] float leverVolume = 0.8f;
    [Tooltip("Ratchet clicks over a full pull.")]
    [SerializeField] int ratchetClicks = 7;

    static AudioClip _clunk, _click;
    int _lastClickStep;

    XRSimpleInteractable _interactable;
    IXRSelectInteractor _grabbingInteractor;
    float _grabStartLocalY;
    float _currentAngle;
    bool _hasTriggeredThisPull;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (handle == null) handle = transform;
        if (pivot == null) pivot = handle.parent != null ? handle.parent : transform;
    }

    void OnEnable()
    {
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        _grabbingInteractor = args.interactorObject;
        _grabStartLocalY = pivot.InverseTransformPoint(_grabbingInteractor.transform.position).y;
        _hasTriggeredThisPull = false;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        _grabbingInteractor = null;
    }

    void PlayLever(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        if (audioSource == null)
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, volume * leverVolume);
            return;
        }
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume * leverVolume);
    }

    // One ratchet tooth: a very short, bright metallic tick.
    static AudioClip Click()
    {
        if (_click != null) return _click;
        const int rate = 44100;
        var data = new float[(int)(rate * 0.035f)];
        var random = new System.Random(11);
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            float ring = Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.3f;
            data[i] = (noise * 0.5f + ring) * Mathf.Exp(-t * 160f) * 0.7f;
        }
        _click = AudioClip.Create("Lever Click", data.Length, 1, rate, false);
        _click.SetData(data, 0);
        return _click;
    }

    // The lever hitting its stop and the mechanism letting go: a hard
    // metallic knock, a low body thump, then a short spring rattle.
    static AudioClip Clunk()
    {
        if (_clunk != null) return _clunk;
        const int rate = 44100;
        var data = new float[(int)(rate * 0.55f)];
        var random = new System.Random(5);
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.2f;

            float knock = (noise * 0.6f + Mathf.Sin(2f * Mathf.PI * 1250f * t) * 0.5f) * Mathf.Exp(-t * 70f);
            float thump = Mathf.Sin(2f * Mathf.PI * (95f - 30f * Mathf.Min(1f, t * 8f)) * t) * Mathf.Exp(-t * 16f) * 0.9f;
            // Second, softer knock as the catch releases, then the spring.
            float t2 = t - 0.09f;
            float release = t2 > 0f ? (low * 1.2f + Mathf.Sin(2f * Mathf.PI * 820f * t2) * 0.35f) * Mathf.Exp(-t2 * 45f) : 0f;
            float t3 = t - 0.12f;
            float spring = t3 > 0f
                ? Mathf.Sin(2f * Mathf.PI * (310f + 40f * Mathf.Sin(t3 * 55f)) * t3) * Mathf.Exp(-t3 * 9f) * 0.18f
                : 0f;
            data[i] = Mathf.Clamp(knock + thump + release + spring, -1f, 1f) * 0.85f;
        }
        _clunk = AudioClip.Create("Lever Clunk", data.Length, 1, rate, false);
        _clunk.SetData(data, 0);
        return _clunk;
    }

    void Update()
    {
        if (_grabbingInteractor != null)
        {
            float currentLocalY = pivot.InverseTransformPoint(_grabbingInteractor.transform.position).y;
            float pulledDistance = Mathf.Clamp(_grabStartLocalY - currentLocalY, 0f, maxPullDistance);
            _currentAngle = (pulledDistance / maxPullDistance) * maxPullAngle;

            // A click each time the lever passes another notch on the way down.
            int step = Mathf.FloorToInt(_currentAngle / maxPullAngle * Mathf.Max(1, ratchetClicks));
            if (step > _lastClickStep && !_hasTriggeredThisPull) PlayLever(Click(), 0.45f, 0.9f + step * 0.03f);
            _lastClickStep = step;

            if (!_hasTriggeredThisPull && _currentAngle >= maxPullAngle * pullThreshold)
            {
                _hasTriggeredThisPull = true;
                PlayLever(customSpinClip != null ? customSpinClip : Clunk(), 1f, 1f);
                if (slotMachine != null) slotMachine.Spin();
            }
        }
        else
        {
            _currentAngle = Mathf.MoveTowards(_currentAngle, 0f, returnSpeed * Time.deltaTime);
            _lastClickStep = 0;
        }

        float appliedAngle = invertRotation ? -_currentAngle : _currentAngle;
        handle.localRotation = Quaternion.AngleAxis(appliedAngle, hingeAxis);
    }
}
