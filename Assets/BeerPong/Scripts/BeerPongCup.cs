using UnityEngine;

// One cup in a team's 3-2-1 triangle. The cup's own MeshCollider is solid
// walls the ball can bounce off and rattle around in; the child trigger
// ("Score Zone") sits low inside the cup, so only a ball that actually drops
// in counts - one resting on the rim never reaches it. BeerPongBall does the
// trigger detection (it owns the Rigidbody) and reports here.
public class BeerPongCup : MonoBehaviour
{
    [Tooltip("0 = Red team's cup, 1 = Blue team's cup. Hitting it scores for the other team.")]
    [SerializeField] int side;

    BeerPongTable _table;

    public int Side => side;
    public bool IsStanding => gameObject.activeSelf;
    // Where this cup sits in the full rack - a re-rack moves cups away from
    // it, a new game puts them back.
    public Vector3 HomeLocalPosition { get; private set; }

    void Awake()
    {
        _table = GetComponentInParent<BeerPongTable>();
        HomeLocalPosition = transform.localPosition;
    }

    public void Hit()
    {
        if (!IsStanding || _table == null) return;
        _table.OnCupHit(this);
    }

    public void Remove() => gameObject.SetActive(false);

    public void Restore()
    {
        transform.localPosition = HomeLocalPosition;
        gameObject.SetActive(true);
    }
}
