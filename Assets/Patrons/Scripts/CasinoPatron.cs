using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A guest who strolls around the casino: walks (on the baked NavMesh) to
// somewhere interesting - a table to watch, the bar, a row of slots, or just
// a spot on the floor - lingers there a while looking at the action, then
// picks somewhere else. Stops politely if the player is right in the way.
//
// Not bumping into each other: they steer around one another with the
// NavMesh's high-quality avoidance, someone standing still at a spot is
// "heavier" than someone walking (walkers go round them), and when two
// walkers meet head-on, or one catches up with another, one of them waits a
// moment and lets the other pass. Anyone stuck for a few seconds gives up
// and goes somewhere else.
public class CasinoPatron : NpcCharacter
{
    static readonly System.Collections.Generic.List<CasinoPatron> All = new System.Collections.Generic.List<CasinoPatron>();
    const int WalkingPriority = 50, StandingPriority = 20; // lower = others give way to them

    NavMeshAgent _agent;
    CasinoPatrons.Spot _spot;
    float _lingerUntil, _blockedUntil, _walkClipSpeed = 1.1f, _repathAt, _stuckFor;
    bool _walking;
    int _rank;

    // Talking. Two guests who end up near each other sometimes stop for a
    // short chat (one bubble at a time, looking at each other), then both
    // walk off somewhere new. The player can select a guest to hear their
    // next line; they stop and face the player for a while.
    const float ChatRange = 1.6f, TalkHold = 8f;
    static float s_nextChatStart;
    PatronDialogue _dialogue;
    List<string> _toPlayer;
    int _talkIndex;
    float _talkUntil = -1f, _lastTalk = -999f, _chatCooldown, _nextChatCheck;
    CasinoPatron _partner;
    Coroutine _chat;

    public bool Walking => _walking;
    bool Halted => _partner != null || Time.time < _talkUntil;

    public void Init(CasinoPatrons manager, RuntimeAnimatorController controller, float walkClipSpeed)
    {
        Init(manager, controller);
        _walkClipSpeed = Mathf.Max(0.3f, walkClipSpeed);
        _agent = gameObject.AddComponent<NavMeshAgent>();
        _agent.agentTypeID = 0;
        _agent.radius = 0.3f; // personal space for steering around others (the path itself uses the baked 0.22 m)
        _agent.height = 1.7f;
        _agent.speed = Random.Range(0.85f, 1.15f);
        _agent.acceleration = 3f;
        _agent.angularSpeed = 300f;
        _agent.stoppingDistance = 0.1f;
        _agent.autoBraking = true;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        _agent.avoidancePriority = WalkingPriority;
        _rank = All.Count;
        All.Add(this);
        if (NavMesh.SamplePosition(transform.position, out var hit, 1f, NavMesh.AllAreas)) _agent.Warp(hit.position);
        _lingerUntil = Time.time + Random.Range(1f, 8f);
        SetPose(PoseIdle);
        _dialogue = new PatronDialogue();
        _toPlayer = _dialogue.ToPlayer();
        _chatCooldown = Time.time + Random.Range(8f, 25f);
    }

    void Update()
    {
        if (_agent == null || !_agent.isOnNavMesh) return;

        if (Halted)
        {
            Vector3 face = _partner != null ? _partner.transform.position : PlayerHead != null ? PlayerHead.position : transform.position + transform.forward;
            FaceToward(face, 5f);
            return;
        }
        if (Time.time >= _nextChatCheck)
        {
            _nextChatCheck = Time.time + 0.5f;
            TryStartChat();
        }

        if (!_walking)
        {
            if (_spot != null) FaceToward(_spot.LookAt, 2.5f);
            if (Time.time >= _lingerUntil) GoSomewhere();
            return;
        }

        // Someone (the player) is standing right in front of us: wait.
        var head = PlayerHead;
        if (head != null)
        {
            Vector3 to = head.position - transform.position;
            to.y = 0f;
            bool inTheWay = to.sqrMagnitude < 0.8f * 0.8f && Vector3.Dot(transform.forward, to.normalized) > 0.5f;
            if (inTheWay) _blockedUntil = Time.time + 0.8f;
        }
        if (ShouldGiveWay()) _blockedUntil = Mathf.Max(_blockedUntil, Time.time + 0.6f);
        bool waiting = Time.time < _blockedUntil;
        _agent.isStopped = waiting;
        _agent.avoidancePriority = WalkingPriority; // back to walking after a chat

        float speed = _agent.velocity.magnitude;

        // Wedged against someone (not just politely waiting): try somewhere else.
        _stuckFor = !waiting && speed < 0.1f && !_agent.pathPending ? _stuckFor + Time.deltaTime : 0f;
        if (_stuckFor > 4f)
        {
            _stuckFor = 0f;
            Arrive();
            _lingerUntil = Time.time + Random.Range(0.5f, 2f);
            return;
        }
        if (speed > 0.08f)
        {
            SetPose(PoseWalk);
            Anim.speed = Mathf.Clamp(speed / _walkClipSpeed, 0.5f, 1.6f);
        }
        else
        {
            SetPose(PoseIdlePlain);
            Anim.speed = 1f;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.05f)
        {
            Arrive();
            return;
        }
        // Stuck for some reason (path blocked by others): try again later.
        if (Time.time > _repathAt)
        {
            _repathAt = Time.time + 20f;
            if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) Arrive();
        }
    }

    // Another walker close ahead: if we're behind them, or it's head-on and
    // they have right of way, stop for a moment.
    bool ShouldGiveWay()
    {
        Vector3 me = transform.position, heading = _agent.desiredVelocity;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.01f) return false;
        heading.Normalize();
        foreach (var other in All)
        {
            if (other == this || other == null || !other._walking) continue;
            Vector3 to = other.transform.position - me;
            to.y = 0f;
            float d2 = to.sqrMagnitude;
            if (d2 > 0.9f * 0.9f || d2 < 0.0001f) continue;
            Vector3 dir = to / Mathf.Sqrt(d2);
            if (Vector3.Dot(heading, dir) < 0.55f) continue; // not in front of us

            Vector3 theirHeading = other._agent != null ? other._agent.desiredVelocity : Vector3.zero;
            theirHeading.y = 0f;
            bool theySeeUs = theirHeading.sqrMagnitude > 0.01f && Vector3.Dot(theirHeading.normalized, -dir) > 0.55f;
            if (!theySeeUs) return true;     // we're catching up from behind (or they're crossing): wait
            if (other.IsWaiting) continue; // they're already waiting for us: go round them
            if (other._rank < _rank) return true; // head-on: the newer one gives way
        }
        return false;
    }

    bool IsWaiting => Time.time < _blockedUntil;

    void GoSomewhere()
    {
        var next = Manager.TakeSpot(this, _spot);
        if (next == null) { _lingerUntil = Time.time + 3f; return; }
        _spot = next;
        if (!_agent.SetDestination(next.Position)) { _lingerUntil = Time.time + 2f; return; }
        _walking = true;
        _agent.avoidancePriority = WalkingPriority;
        _repathAt = Time.time + 20f;
    }

    void Arrive()
    {
        _walking = false;
        _agent.avoidancePriority = StandingPriority;
        _agent.isStopped = false;
        _agent.ResetPath();
        Anim.speed = 1f;
        SetPose(_spot != null && _spot.Kind == CasinoPatrons.SpotKind.Bar ? PoseIdlePlain : PoseIdle);
        float linger = _spot != null ? Random.Range(_spot.MinLinger, _spot.MaxLinger) : 5f;
        if (InPlayZone) linger = 0.3f; // gave up in front of a beer pong table: move straight on
        _lingerUntil = Time.time + linger;
    }

    // Standing where a beer pong player would stand.
    bool InPlayZone => Manager != null && Manager.InPlayZone(transform.position);

    // ------------------------------------------------------------ talking

    bool CanChat => _partner == null && Time.time >= _talkUntil && Time.time >= _chatCooldown && _agent != null && _agent.isOnNavMesh;

    void TryStartChat()
    {
        if (!CanChat || Speaking || Time.time < s_nextChatStart || Random.value > 0.3f) return;
        if (InPlayZone) return;
        foreach (var other in All)
        {
            if (other == this || other == null || !other.CanChat || other.InPlayZone) continue;
            Vector3 to = other.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d > ChatRange || d < 0.5f) continue;
            s_nextChatStart = Time.time + 6f; // not everyone at once
            _chat = StartCoroutine(Chat(other));
            return;
        }
    }

    IEnumerator Chat(CasinoPatron other)
    {
        _partner = other;
        other._partner = this;
        Halt();
        other.Halt();
        yield return new WaitForSeconds(0.7f); // turn to each other first

        foreach (var (aSpeaks, line) in PatronDialogue.Chat(_dialogue, other._dialogue))
        {
            if (other == null || _partner != other) yield break;
            var speaker = aSpeaks ? this : other;
            var listener = aSpeaks ? other : this;
            listener.HideSpeech();
            float t = ReadTime(line);
            speaker.Say(line, t + 0.3f);
            yield return new WaitForSeconds(t);
        }
        yield return new WaitForSeconds(0.5f);
        _chat = null;
        EndChat(true);
    }

    // Stops a chat for both; walkOff sends them somewhere new.
    void EndChat(bool walkOff)
    {
        var other = _partner;
        LeaveChat(walkOff);
        if (other != null && other._partner == this) other.LeaveChat(walkOff);
    }

    void LeaveChat(bool walkOff)
    {
        if (_chat != null) { StopCoroutine(_chat); _chat = null; }
        _partner = null;
        _chatCooldown = Time.time + Random.Range(40f, 80f);
        if (walkOff && _agent != null && _agent.isOnNavMesh) GoSomewhere();
    }

    // Stand still where we are (the walk resumes on its own afterwards).
    void Halt()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            _agent.avoidancePriority = StandingPriority;
        }
        Anim.speed = 1f;
        SetPose(PoseIdlePlain);
        _stuckFor = 0f;
    }

    protected override void OnTalkedTo()
    {
        // The player takes over from whoever we were chatting with.
        if (_partner != null)
        {
            var other = _partner;
            EndChat(false);
            if (other != null) { other.HideSpeech(); other.GoSomewhere(); }
        }
        if (Time.time - _lastTalk > 25f) _talkIndex = 0; // been a while: start over with hello
        _lastTalk = Time.time;
        Halt();
        _talkUntil = Time.time + TalkHold;
        string line = _toPlayer[_talkIndex++ % _toPlayer.Count];
        Say(line, Mathf.Max(ReadTime(line), 4f));
        // Someone lingering at a spot stays a moment after the talk rather than walking off at once.
        if (!_walking) _lingerUntil = Mathf.Max(_lingerUntil, _talkUntil + 2f);
    }

    protected override bool WantsToLook(out Vector3 target)
    {
        if (_partner != null) { target = _partner.HeadPosition; return true; }
        if (Time.time < _talkUntil && PlayerHead != null) { target = PlayerHead.position; return true; }
        return base.WantsToLook(out target);
    }

    void FaceToward(Vector3 point, float speed)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    void OnDestroy()
    {
        if (_partner != null && _partner._partner == this) _partner.LeaveChat(true);
        All.Remove(this);
        if (Manager != null) Manager.ReleaseSpot(this);
    }
}
