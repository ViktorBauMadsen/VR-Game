using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// The casino's front door, where a run begins.
//
// "Start game" (CasinoRun.StartRun) puts every player on one of the spawn
// points outside and shuts the doors. Each player presses a door (point at
// or touch it and squeeze grip) to say they are ready; when everyone has,
// the doors swing open and day 1's clock starts. Outside of that moment the
// doors simply stand open, so the hub is never locked.
//
// Built by Tools > Casino > Build Run Entrance. The player waits on the
// root's +Z side; -Z is the inside of the casino.
public class RunEntrance : MonoBehaviour
{
    public static RunEntrance Instance { get; private set; }

    [Tooltip("Where players appear, facing the door. One per player.")]
    [SerializeField] Transform[] spawnPoints = new Transform[0];
    [Tooltip("The pivots the door leaves swing on.")]
    [SerializeField] Transform[] hinges = new Transform[0];
    [Tooltip("Y rotation of each hinge when open (closed is 0).")]
    [SerializeField] float[] openAngles = new float[0];
    [SerializeField] WorldTextPanel sign;
    [SerializeField] float swingSpeed = 140f; // degrees per second

    float[] _angles;
    string _shown;

    void Awake()
    {
        if (Instance == null) Instance = this;
        _angles = new float[hinges.Length];
        for (int i = 0; i < hinges.Length; i++) _angles[i] = OpenAngle(i); // open until a run gathers
        foreach (var interactable in GetComponentsInChildren<XRSimpleInteractable>(true))
            interactable.selectEntered.AddListener(OnPressed);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    float OpenAngle(int i) => i < openAngles.Length ? openAngles[i] : 0f;

    void OnPressed(SelectEnterEventArgs args)
    {
        var run = CasinoRun.Instance;
        if (run == null || run.Phase != RunPhase.Gathering) return;
        bool first = !run.LocalReady;
        run.PressDoor();
        if (args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(first ? 0.5f : 0.2f, 0.1f);
    }

    void Update()
    {
        var run = CasinoRun.Instance;
        bool closed = run != null && run.Phase == RunPhase.Gathering;
        for (int i = 0; i < hinges.Length; i++)
        {
            if (hinges[i] == null) continue;
            _angles[i] = Mathf.MoveTowards(_angles[i], closed ? 0f : OpenAngle(i), swingSpeed * Time.deltaTime);
            hinges[i].localRotation = Quaternion.Euler(0f, _angles[i], 0f);
        }

        if (sign == null) return;
        string text;
        if (run == null || run.Phase == RunPhase.FreePlay || run.Phase == RunPhase.Dead)
            text = "<color=#FFD24A>THE CASINO</color>\n<size=50%>START A GAME FROM THE MENU (Y)</size>";
        else if (run.Phase == RunPhase.Gathering)
            text = (run.LocalReady ? "<color=#5CFF7A>YOU'RE READY</color>" : "<color=#FFD24A>PRESS THE DOOR TO START</color>")
                + $"\n<size=50%>{run.ReadyCount} / {run.Players} READY   -   DAY {run.Day} QUOTA ${run.Quota:N0}</size>";
        else
            text = $"<color=#FFD24A>DAY {run.Day}</color>\n<size=50%>QUOTA ${run.Quota:N0}</size>";
        if (run != null && run.TodayEvent != DayEvent.None && (run.Phase == RunPhase.Gathering || run.Phase == RunPhase.Running))
            text += $"\n<size=42%><color={(CasinoRun.EventIsGood(run.TodayEvent) ? "#5CFF7A" : "#FF6A5C")}>TODAY: {CasinoRun.EventTitle(run.TodayEvent)}</color></size>";
        if (text == _shown) return;
        _shown = text;
        sign.SetText(text);
    }

    // Puts the local player on their spawn point, looking at the door.
    public void MovePlayerToSpawn(int slot)
    {
        if (spawnPoints.Length == 0) return;
        var point = spawnPoints[Mathf.Abs(slot) % spawnPoints.Length];
        var origin = FindAnyObjectByType<XROrigin>();
        if (point == null || origin == null) return;

        // A CharacterController would drag the rig straight back.
        var controller = origin.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;

        Vector3 forward = Vector3.ProjectOnPlane(point.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f) origin.MatchOriginUpCameraForward(Vector3.up, forward.normalized);
        origin.MoveCameraToWorldLocation(point.position + Vector3.up * origin.CameraInOriginSpaceHeight);

        if (wasEnabled) controller.enabled = true;
    }
}
