using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// One of the buttons on the Token Stall's counter. Same "XR Simple
// Interactable + cap that travels down" button as the blackjack table's.
//
// Three of them are shelf slots: each sells whichever powerup is in that
// slot of today's stock (CasinoRun.Offer - three random powerups, new every
// day). The fourth rerolls the stock for tokens. The card under the button
// (on the front of the counter) says what it does, what it costs and
// whether you can press it right now.
[RequireComponent(typeof(XRSimpleInteractable))]
public class TokenStallButton : MonoBehaviour
{
    [SerializeField] TokenStall stall;
    [Tooltip("Which slot of today's stock this button sells (0-2). -1 = work it out from the button's place on the counter.")]
    [SerializeField] int slot = -1;
    [Tooltip("This is the reroll button instead of a shelf slot.")]
    [SerializeField] bool reroll;
    [SerializeField] Transform cap;
    [SerializeField] WorldTextPanel card;
    [SerializeField] float pressDepth = 0.01f;
    [SerializeField] float pressSpeed = 0.15f;

    XRSimpleInteractable _interactable;
    Vector3 _restLocalPos;
    bool _pressed;
    float _nextRefresh;
    string _shown;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (cap != null) _restLocalPos = cap.localPosition;

        // A stall built before the shop had random stock: the first three
        // buttons become the shelf slots and the last one the reroll.
        if (slot < 0 && !reroll && stall != null)
        {
            var all = stall.GetComponentsInChildren<TokenStallButton>(true);
            int index = System.Array.IndexOf(all, this);
            if (index >= CasinoRun.OfferSize) reroll = true;
            else slot = Mathf.Max(0, index);
        }
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
        _pressed = true;
        _nextRefresh = 0f;
        var run = CasinoRun.Instance;
        if (stall == null || run == null) return;
        if (reroll) stall.Reroll(args.interactorObject);
        else stall.Buy(run.Offer[Mathf.Clamp(slot, 0, CasinoRun.OfferSize - 1)], args.interactorObject);
    }

    void OnSelectExited(SelectExitEventArgs args) => _pressed = false;

    void Update()
    {
        if (cap != null)
        {
            Vector3 target = _restLocalPos + (_pressed ? Vector3.down * pressDepth : Vector3.zero);
            cap.localPosition = Vector3.MoveTowards(cap.localPosition, target, pressSpeed * Time.deltaTime);
        }

        if (card == null || Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.3f;

        var run = CasinoRun.Instance;
        if (run == null) return;

        string text;
        if (reroll)
        {
            int price = run.RerollPrice;
            string blocked = run.WhyNotReroll();
            string state = blocked == null ? "<color=#5CFF7A>PRESS TO REROLL</color>" : $"<color=#FF6A5C>{blocked}</color>";
            text = "<color=#FFD24A><b>REROLL THE SHOP</b></color>\n"
                + "<size=62%>Don't like today's three? Swap them for three new random powerups. The price doubles with every reroll today.</size>\n"
                + $"<size=80%><b>{price} TOKEN{(price == 1 ? "" : "S")}</b></size>\n"
                + $"<size=62%>{state}</size>";
        }
        else
        {
            var powerup = run.Offer[Mathf.Clamp(slot, 0, CasinoRun.OfferSize - 1)];
            int price = run.Price(powerup);
            string blocked = run.WhyNot(powerup);
            string owned = run.Owned(powerup);
            string state = blocked == null ? "<color=#5CFF7A>PRESS TO BUY</color>" : $"<color=#FF6A5C>{blocked}</color>";
            text = $"<color=#FFD24A><b>{CasinoRun.Title(powerup)}</b></color>\n"
                + $"<size=62%>{run.Describe(powerup)}</size>\n"
                + $"<size=80%><b>{price} TOKEN{(price == 1 ? "" : "S")}</b></size>\n"
                + $"<size=62%>{state}{(owned.Length > 0 ? "   (" + owned + ")" : "")}</size>";
        }
        if (text == _shown) return;
        _shown = text;
        card.SetText(text);
    }
}
