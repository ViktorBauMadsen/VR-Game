using UnityEngine;

// What every slot machine has in common, so the lever (SlotMachineLever) and
// the -/+ bet panel (SlotBetPanel) work with any kind of machine: the
// classic SlotMachine and the video slots (GoldenInfernoSlot, StarWheelSlot).
// Owns the bet: the bet ladder, raising/lowering it, and the events the bet
// panel listens to.
public abstract class SlotMachineBase : MonoBehaviour
{
    [Header("Bet")]
    [SerializeField] protected int betAmount = 10; // starting bet; the player changes it with the bet panel
    // The bets the -/+ buttons step through, lowest to highest.
    [SerializeField] protected int[] betLadder = { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };
    [Tooltip("Build the -/+ bet panel under the screen at runtime.")]
    [SerializeField] protected bool buildBetPanel = true;
    [Tooltip("Nudge the bet panel, in meters: x = right, y = up, z = toward the player.")]
    [SerializeField] protected Vector3 betPanelOffset = Vector3.zero;

    public int BetAmount => betAmount;
    // Busy = the bet can't change right now (reels spinning, a feature playing...).
    public abstract bool IsSpinning { get; }
    public virtual bool CanBetDown => !IsSpinning && betAmount > betLadder[0];
    public virtual bool CanBetUp => !IsSpinning && betAmount < betLadder[betLadder.Length - 1];

    public event System.Action<int> BetChanged;      // new bet amount
    public event System.Action SpinStateChanged;     // started or finished spinning
    public event System.Action SpinRejected;         // not enough balance for the bet

    // The lever was pulled.
    public abstract void Spin();

    // Where the bet panel goes: forward points INTO the machine (the panel's
    // face looks at the player), up is screen-up.
    public abstract bool TryGetBetPanelPose(Vector3 offset, out Vector3 position, out Quaternion facing);

    protected void RaiseSpinStateChanged() => SpinStateChanged?.Invoke();
    protected void RaiseSpinRejected() => SpinRejected?.Invoke();

    // steps = +1 to raise the bet one rung of the ladder, -1 to lower it. Locked mid-spin.
    public void ChangeBet(int steps)
    {
        if (IsSpinning) return;
        int rung = Mathf.Clamp(System.Array.IndexOf(betLadder, ClampBet(betAmount)) + steps, 0, betLadder.Length - 1);
        int newBet = betLadder[rung];
        if (newBet == betAmount) return;
        betAmount = newBet;
        BetChanged?.Invoke(betAmount);
        OnBetChanged();
    }

    // For the machine's own reaction (a click sound, meters...).
    protected virtual void OnBetChanged() { }

    protected int ClampBet(int bet)
    {
        // The nearest rung of the ladder.
        if (betLadder == null || betLadder.Length == 0) betLadder = new[] { 10 };
        int best = betLadder[0];
        foreach (int rung in betLadder)
            if (Mathf.Abs(rung - bet) < Mathf.Abs(best - bet)) best = rung;
        return best;
    }

    protected void BuildBetPanelIfWanted()
    {
        if (buildBetPanel) SlotBetPanel.Build(this, betPanelOffset);
    }

    // Shows where the bet panel will appear (yellow box) when this machine
    // is selected, so betPanelOffset can be tuned without entering Play mode.
    protected virtual void OnDrawGizmosSelected()
    {
        if (!buildBetPanel) return;
        if (!TryGetBetPanelPose(betPanelOffset, out Vector3 pos, out Quaternion rot)) return;
        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(pos, rot, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.345f, 0.085f, 0.02f));
    }

    [ContextMenu("Debug Bet Up")]
    void DebugBetUp() => ChangeBet(+1);

    [ContextMenu("Debug Bet Down")]
    void DebugBetDown() => ChangeBet(-1);
}
