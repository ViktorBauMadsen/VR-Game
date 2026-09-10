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
    [SerializeField] AudioClip pullClip; // plays once the lever has been pulled far enough to trigger a spin

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

    void Update()
    {
        if (_grabbingInteractor != null)
        {
            float currentLocalY = pivot.InverseTransformPoint(_grabbingInteractor.transform.position).y;
            float pulledDistance = Mathf.Clamp(_grabStartLocalY - currentLocalY, 0f, maxPullDistance);
            _currentAngle = (pulledDistance / maxPullDistance) * maxPullAngle;

            if (!_hasTriggeredThisPull && _currentAngle >= maxPullAngle * pullThreshold)
            {
                _hasTriggeredThisPull = true;
                if (audioSource != null && pullClip != null) audioSource.PlayOneShot(pullClip);
                if (slotMachine != null) slotMachine.Spin();
            }
        }
        else
        {
            _currentAngle = Mathf.MoveTowards(_currentAngle, 0f, returnSpeed * Time.deltaTime);
        }

        float appliedAngle = invertRotation ? -_currentAngle : _currentAngle;
        handle.localRotation = Quaternion.AngleAxis(appliedAngle, hingeAxis);
    }
}
