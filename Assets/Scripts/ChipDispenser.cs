using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Sits at a fixed pocket point (a child of PlayerTorsoAnchor). Chips are
// infinite, so this never runs out - on grab it hands the interactor's grip
// off to a freshly spawned chip instead of moving itself, the same
// "reacts to select but doesn't come away in hand" trick
// SlotMachineLever/RouletteSpinButton use via an XR Simple Interactable
// rather than an XR Grab Interactable. No selectExited listener: the stub
// has no visual state to reset, so the synthetic exit our own SelectExit
// call below triggers is harmless to ignore.
[RequireComponent(typeof(XRSimpleInteractable))]
public class ChipDispenser : MonoBehaviour
{
    [SerializeField] CasinoChip chipPrefab;
    [SerializeField] Transform spawnPoint; // defaults to this transform

    XRSimpleInteractable _interactable;
    XRInteractionManager _manager;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (spawnPoint == null) spawnPoint = transform;
    }

    // Not resolved in Awake: the chip prefabs don't have
    // XRGrabInteractable.interactionManager wired up, and neither scene has
    // an explicit "XR Interaction Manager" object, so this project relies
    // entirely on XRI's lazy auto-create-on-first-enable fallback. Awake
    // runs for every object before OnEnable runs for any of them, so
    // looking here could run before that fallback has created anything.
    // By Start, this object's own XRSimpleInteractable.OnEnable has
    // already registered with (and if needed, created) the manager.
    void Start()
    {
        _manager = FindAnyObjectByType<XRInteractionManager>();
    }

    void OnEnable() => _interactable.selectEntered.AddListener(OnSelectEntered);
    void OnDisable() => _interactable.selectEntered.RemoveListener(OnSelectEntered);

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (chipPrefab == null || _manager == null) return;

        var interactor = args.interactorObject;
        _manager.SelectExit(interactor, _interactable);

        var chip = Instantiate(chipPrefab, spawnPoint.position, spawnPoint.rotation);
        // Unconditional: a just-Instantiated interactable isn't in the
        // interactor's per-frame computed valid-targets list yet, so the
        // plain (checked) SelectEnter would be silently ignored.
        _manager.SelectEnterUnconditionally(interactor, chip.GetComponent<XRGrabInteractable>());
    }
}
