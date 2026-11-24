using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The casino's guests. At start this puts the baked NavMesh in, works out
// where the interesting places are (around every game table, the bar, the
// slot machines, the dartboard, open floor), and spawns:
//
//   - patronCount strollers (CasinoPatron) who wander from place to place,
//   - one regular (SlotGambler) who always sits at the same slot machine,
//   - James Bond (BondNpc) on his stool at the bar.
//
// Built by Tools > Casino > Build Patrons and Bond (SetupPatrons), which also
// makes the animator controller, the Bond outfit and the NavMesh asset.
// Everything here is local scenery - in multiplayer each player sees their
// own crowd.
public class CasinoPatrons : MonoBehaviour
{
    public enum SpotKind { Table, Bar, Slots, Darts, Floor }

    public class Spot
    {
        public Vector3 Position, LookAt;
        public SpotKind Kind;
        public float MinLinger, MaxLinger, Weight;
        public CasinoPatron Occupant;
    }

    [Header("Built by SetupPatrons")]
    [SerializeField] NavMeshData navMesh;
    [SerializeField] RuntimeAnimatorController controller;
    [Tooltip("The strollers' looks. Parallel to controllers (an override per body type, or empty for the default).")]
    [SerializeField] GameObject[] patronPrefabs = new GameObject[0];
    [SerializeField] RuntimeAnimatorController[] patronControllers = new RuntimeAnimatorController[0];
    [SerializeField] GameObject gamblerPrefab;
    [SerializeField] GameObject bondPrefab;
    [SerializeField] Material stoolSeatMaterial;
    [SerializeField] Material stoolMetalMaterial;
    [SerializeField] Material glassMaterial;
    [SerializeField] Material oliveMaterial;
    [Tooltip("Rigged characters for the staff (StaffLook): the blackjack dealers, and the croupier + bartender.")]
    [SerializeField] GameObject dealerLook;
    [SerializeField] GameObject staffLook;
    [Tooltip("The bartender's own look (white shirt, waistcoat, bow tie, moustache). Falls back to staffLook.")]
    [SerializeField] GameObject bartenderLook;

    [Header("Crowd")]
    [SerializeField, Range(0, 12)] int patronCount = 6;
    [Tooltip("Which slot machine the regular sits at (slot machines sorted by position). -1 = pick a Golden Inferno if there is one.")]
    [SerializeField] int gamblerMachine = -1;
    [Tooltip("Height of the seat the sitting animations were made for (the hips sit this high above the feet).")]
    [SerializeField] float animationSeatHeight = 0.48f;
    [Tooltip("Hall interior, for random strolling spots.")]
    [SerializeField] Bounds hall = new Bounds(new Vector3(-2.9f, 0.42f, 13.87f), new Vector3(14f, 1f, 28.2f));

    const float MinSpotGap = 0.85f; // two people lingering side by side don't touch

    readonly List<Spot> _spots = new List<Spot>();
    readonly List<Vector3> _seated = new List<Vector3>();
    readonly List<GameObject> _spawned = new List<GameObject>();
    NavMeshDataInstance _navInstance;
    Transform _playerHead;

    public Transform PlayerHead
    {
        get
        {
            if (_playerHead == null && Camera.main != null) _playerHead = Camera.main.transform;
            return _playerHead;
        }
    }

    void OnEnable()
    {
        if (navMesh != null && !_navInstance.valid) _navInstance = NavMesh.AddNavMeshData(navMesh);
    }

    void OnDisable()
    {
        if (_navInstance.valid) _navInstance.Remove();
        _navInstance = default;
    }

    IEnumerator Start()
    {
        // Let the slot machines and the bar build themselves first.
        yield return null;
        yield return null;
        DressStaff();
        bool canWalk = navMesh != null && _navInstance.valid;
        // The sitters first, so the strollers' spots keep clear of them.
        SpawnBond();
        SpawnGambler();
        if (canWalk) FindSpots();
        else if (patronCount > 0) Debug.LogWarning("CasinoPatrons: no NavMesh - run Tools > Casino > Build Patrons and Bond. Strollers skipped.");
        if (canWalk && _spots.Count > 0) SpawnStrollers();
    }

    // ------------------------------------------------------------ places

    void FindSpots()
    {
        _spots.Clear();
        _playZones.Clear();
        // First, so every later spot keeps out of the beer pong play zones.
        foreach (var table in FindObjectsByType<BeerPongTable>()) BeerPongSpots(table.transform);
        foreach (var table in FindObjectsByType<RouletteTable>()) Ring(table.transform, SpotKind.Table, 8);
        foreach (var table in FindObjectsByType<BlackjackTable>()) Ring(table.transform, SpotKind.Table, 6);

        foreach (var board in FindObjectsByType<Dartboard>())
        {
            Vector3 f = Flat(board.transform.forward), r = Vector3.Cross(Vector3.up, f);
            foreach (float side in new[] { -1.3f, 1.3f })
                Add(board.transform.position + f * 2.2f + r * side, board.transform.position, SpotKind.Darts, 8f, 20f, 1f);
        }

        foreach (var bartender in FindObjectsByType<Bartender>())
        {
            Transform bar = bartender.transform.parent != null ? bartender.transform.parent : bartender.transform;
            Vector3 f = Flat(bar.forward), r = Flat(bar.right);
            foreach (float x in new[] { -0.6f, 0f, 0.55f })
                Add(bar.position + f * 0.95f + r * x, bar.position + r * x + Vector3.up * 1.1f, SpotKind.Bar, 15f, 35f, 1.5f);
        }

        foreach (var machine in FindObjectsByType<SlotMachineBase>())
        {
            if (!Front(machine, out Vector3 front, out Vector3 toPlayer)) continue;
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer);
            Add(front + toPlayer * 0.5f + side * 0.6f, machine.transform.position + Vector3.up * 1.2f, SpotKind.Slots, 6f, 16f, 0.5f);
        }

        // Open floor, for just walking about.
        for (int i = 0, made = 0; i < 80 && made < 10; i++)
        {
            Vector3 p = hall.center + new Vector3(Random.Range(-hall.extents.x, hall.extents.x), 0f, Random.Range(-hall.extents.z, hall.extents.z));
            if (Add(p, p + Random.insideUnitSphere.normalized * 3f, SpotKind.Floor, 2f, 6f, 0.6f) != null) made++;
        }
    }

    void Ring(Transform table, SpotKind kind, int count)
    {
        if (!RendererBounds(table, out Bounds b)) return;
        float rx = b.extents.x + 0.65f, rz = b.extents.z + 0.65f;
        for (int i = 0; i < count; i++)
        {
            float a = (i + 0.5f) / count * Mathf.PI * 2f;
            Vector3 p = new Vector3(b.center.x + Mathf.Cos(a) * rx, b.min.y, b.center.z + Mathf.Sin(a) * rz);
            Add(p, new Vector3(b.center.x, b.max.y, b.center.z), kind, 10f, 25f, 1.2f);
        }
    }

    // ------------------------------------------------- beer pong play zones

    // The two ends of a beer pong table are where the throwers stand, so
    // nobody may linger or stop to chat there (walking through is fine).
    // Onlookers get spots along the long sides instead.
    struct PlayZone { public Vector3 Centre, Axis, Side; public float HalfLength, HalfWidth; }
    readonly List<PlayZone> _playZones = new List<PlayZone>();
    const float PlayZoneDepth = 1.8f;  // how far out from each end the thrower's area reaches
    const float PlayZoneMargin = 0.45f; // sideways, beyond the table's edge

    void BeerPongSpots(Transform table)
    {
        // The table's length runs along its local Z (cups at both ends).
        Vector3 axis = Flat(table.forward), side = Vector3.Cross(Vector3.up, axis);
        Vector3 centre = table.position;
        float halfLength = 0f, halfWidth = 0f, top = centre.y;
        foreach (var r in table.GetComponentsInChildren<Renderer>())
        {
            Bounds lb = r.localBounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                Vector3 d = r.transform.TransformPoint(corner) - centre;
                halfLength = Mathf.Max(halfLength, Mathf.Abs(Vector3.Dot(d, axis)));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(d, side)));
                top = Mathf.Max(top, centre.y + d.y);
            }
        }
        if (halfLength <= 0f) return;
        _playZones.Add(new PlayZone { Centre = centre, Axis = axis, Side = side, HalfLength = halfLength, HalfWidth = halfWidth });

        Vector3 lookAt = new Vector3(centre.x, top, centre.z);
        foreach (float s in new[] { -1f, 1f })
            foreach (float along in new[] { -0.55f, 0f, 0.55f })
                Add(centre + side * s * (halfWidth + 0.6f) + axis * along * halfLength, lookAt, SpotKind.Table, 10f, 25f, 1.2f);
    }

    // Is p where someone playing beer pong would stand?
    public bool InPlayZone(Vector3 p)
    {
        foreach (var z in _playZones)
        {
            Vector3 d = p - z.Centre;
            d.y = 0f;
            float along = Mathf.Abs(Vector3.Dot(d, z.Axis)), across = Mathf.Abs(Vector3.Dot(d, z.Side));
            if (across < z.HalfWidth + PlayZoneMargin && along > z.HalfLength - 0.25f && along < z.HalfLength + PlayZoneDepth)
                return true;
        }
        return false;
    }

    Spot Add(Vector3 near, Vector3 lookAt, SpotKind kind, float min, float max, float weight)
    {
        if (!NavMesh.SamplePosition(near, out var hit, 0.6f, NavMesh.AllAreas)) return null;
        if (Mathf.Abs(hit.position.y - hall.center.y) > 0.4f) return null; // on a table top, not the floor
        if (InPlayZone(hit.position)) return null; // in front of a beer pong table
        foreach (var s in _spots)
            if ((s.Position - hit.position).sqrMagnitude < MinSpotGap * MinSpotGap) return null;
        foreach (var p in _seated)
            if ((p - hit.position).sqrMagnitude < 0.9f * 0.9f) return null; // the regular / Bond sit here
        var spot = new Spot { Position = hit.position, LookAt = lookAt, Kind = kind, MinLinger = min, MaxLinger = max, Weight = weight };
        _spots.Add(spot);
        return spot;
    }

    // A free place somewhere else, interesting places more likely.
    readonly List<Spot> _choices = new List<Spot>();

    public Spot TakeSpot(CasinoPatron who, Spot current)
    {
        // Mostly somewhere of a different kind than where they are now.
        _choices.Clear();
        float total = 0f;
        foreach (var s in _spots)
        {
            if (s.Occupant != null || s == current) continue;
            if (current != null && s.Kind == current.Kind && Random.value >= 0.3f) continue;
            _choices.Add(s);
            total += s.Weight;
        }
        if (total <= 0f) return null;
        float pick = Random.value * total;
        foreach (var s in _choices)
        {
            pick -= s.Weight;
            if (pick > 0f) continue;
            if (current != null && current.Occupant == who) current.Occupant = null;
            s.Occupant = who;
            return s;
        }
        return null;
    }

    public void ReleaseSpot(CasinoPatron who)
    {
        foreach (var s in _spots)
            if (s.Occupant == who) s.Occupant = null;
    }

    // ------------------------------------------------------------ people

    // The staff get rigged characters in place of their primitive bodies.
    void DressStaff()
    {
        if (controller == null) return;
        foreach (var dealer in FindObjectsByType<BlackjackDealer>()) StaffLook.Dress(dealer, dealerLook != null ? dealerLook : staffLook, controller);
        foreach (var croupier in FindObjectsByType<Croupier>()) StaffLook.Dress(croupier, staffLook, controller);
        foreach (var bartender in FindObjectsByType<Bartender>()) StaffLook.Dress(bartender, bartenderLook != null ? bartenderLook : staffLook, controller);
    }

    void SpawnStrollers()
    {
        var valid = new List<int>();
        for (int i = 0; i < patronPrefabs.Length; i++)
            if (patronPrefabs[i] != null) valid.Add(i);
        if (valid.Count == 0) return;

        // Everyone starts on a different spot, so nobody spawns inside anybody else.
        var starts = new List<Spot>(_spots);
        for (int i = starts.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (starts[i], starts[j]) = (starts[j], starts[i]);
        }
        for (int n = 0; n < patronCount && n < starts.Count; n++)
        {
            int which = valid[n % valid.Count];
            var start = starts[n];
            var go = Instantiate(patronPrefabs[which], start.Position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            go.name = "Patron " + (n + 1) + " (" + patronPrefabs[which].name + ")";
            go.transform.localScale *= Random.Range(0.95f, 1.04f);
            var ctrl = which < patronControllers.Length && patronControllers[which] != null ? patronControllers[which] : controller;
            go.AddComponent<CasinoPatron>().Init(this, ctrl, WalkSpeed(ctrl));
            // The old gentleman walks with a cane (his walk animation grips one).
            if (patronPrefabs[which].name.Contains("Senior") && stoolSeatMaterial != null)
                go.AddComponent<PatronCane>().Init(stoolSeatMaterial, stoolMetalMaterial);
            _spawned.Add(go);
        }
    }

    // How fast the walk animation's feet move, so the legs match the speed.
    static float WalkSpeed(RuntimeAnimatorController ctrl)
    {
        if (ctrl == null) return 1.1f;
        foreach (var clip in ctrl.animationClips)
            if (clip != null && clip.name.Contains("Walk") && clip.averageSpeed.magnitude > 0.2f) return clip.averageSpeed.magnitude;
        return 1.1f;
    }

    void SpawnGambler()
    {
        if (gamblerPrefab == null) return;
        var machines = new List<SlotMachineBase>(FindObjectsByType<SlotMachineBase>());
        machines.RemoveAll(m => m == null || !Front(m, out _, out _));
        if (machines.Count == 0) return;
        machines.Sort((a, b) =>
        {
            int x = a.transform.position.x.CompareTo(b.transform.position.x);
            return x != 0 ? x : a.transform.position.z.CompareTo(b.transform.position.z);
        });
        SlotMachineBase machine = null;
        if (gamblerMachine >= 0) machine = machines[Mathf.Min(gamblerMachine, machines.Count - 1)];
        else
        {
            foreach (var m in machines)
                if (m.GetType().Name.Contains("Inferno")) { machine = m; break; }
            if (machine == null) machine = machines[0];
        }

        Front(machine, out Vector3 front, out Vector3 toPlayer);
        Vector3 seat = front + toPlayer * 0.62f;
        seat.y = Floor(seat);
        var go = Instantiate(gamblerPrefab, seat, Quaternion.LookRotation(-toPlayer), transform);
        go.name = "Slot Regular";
        BuildStool(go.transform.position, animationSeatHeight, transform, "Slot Regular's Stool");
        go.AddComponent<SlotGambler>().Init(this, controller, machine, front);
        AddObstacle(go, 0.38f);
        _spawned.Add(go);
    }

    void SpawnBond()
    {
        if (bondPrefab == null) return;
        var bartender = FindAnyObjectByType<Bartender>();
        if (bartender == null) return;
        Transform bar = bartender.transform.parent != null ? bartender.transform.parent : bartender.transform;

        // His stool: the bar's right-hand one (seen from the customer side), or where it would be.
        Transform stool = null;
        var stools = bar.Find("Stools");
        if (stools != null)
            foreach (Transform s in stools)
                if (stool == null || bar.InverseTransformPoint(s.position).x > bar.InverseTransformPoint(stool.position).x) stool = s;
        Vector3 stoolPos = stool != null ? stool.position : bar.TransformPoint(new Vector3(1.2f, 0f, 0.85f));
        float seatHeight = 0.76f;
        var seat = stool != null ? stool.Find("Seat") : null;
        if (seat != null) seatHeight = seat.position.y - stoolPos.y + seat.lossyScale.y;

        Vector3 facing = -Flat(bar.forward); // customers face the bartender
        Vector3 pos = stoolPos - facing * 0.08f;
        pos.y = stoolPos.y + Mathf.Max(0f, seatHeight - animationSeatHeight);
        var go = Instantiate(bondPrefab, pos, Quaternion.LookRotation(facing), transform);
        go.name = "James Bond";
        var bond = go.AddComponent<BondNpc>();
        bond.Init(this, controller, bartender);
        AddObstacle(go, 0.38f);
        _spawned.Add(go);

        // His martini on the counter in front of him - he picks it up for a sip now and then.
        Vector3 counter = bar.TransformPoint(new Vector3(bar.InverseTransformPoint(stoolPos).x - 0.12f, 1.05f, 0.22f));
        bond.SetMartini(BuildMartini(counter, transform));
    }

    // Someone who sits still: the walkers steer around them, and no stroll spot is put on top of them.
    void AddObstacle(GameObject who, float radius)
    {
        _seated.Add(who.transform.position);
        var obstacle = who.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = radius / Mathf.Max(0.01f, who.transform.lossyScale.x);
        obstacle.height = 1.6f / Mathf.Max(0.01f, who.transform.lossyScale.y);
        obstacle.center = new Vector3(0f, obstacle.height * 0.5f, 0f);
        obstacle.carving = false;
    }

    // ------------------------------------------------------------ props

    void BuildStool(Vector3 floor, float height, Transform parent, string name)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.position = floor;
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, height - 0.02f, 0f), new Vector3(0.38f, 0.03f, 0.38f), stoolSeatMaterial);
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, (height - 0.04f) * 0.5f, 0f), new Vector3(0.05f, (height - 0.04f) * 0.5f, 0.05f), stoolMetalMaterial);
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.012f, 0f), new Vector3(0.34f, 0.012f, 0.34f), stoolMetalMaterial);
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, height * 0.42f, 0f), new Vector3(0.26f, 0.008f, 0.26f), stoolMetalMaterial);
    }

    // A cocktail glass: a cone of glass on a thin stem, with an olive on a pick.
    Transform BuildMartini(Vector3 at, Transform parent)
    {
        var root = new GameObject("Bond's Martini").transform;
        root.SetParent(parent, false);
        root.position = at;
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.002f, 0f), new Vector3(0.06f, 0.002f, 0.06f), glassMaterial);
        Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.045f, 0f), new Vector3(0.008f, 0.043f, 0.008f), glassMaterial);
        var bowl = new GameObject("Bowl");
        bowl.transform.SetParent(root, false);
        bowl.transform.localPosition = new Vector3(0f, 0.088f, 0f);
        bowl.AddComponent<MeshFilter>().sharedMesh = Cone(0.055f, 0.06f, 20);
        var r = bowl.AddComponent<MeshRenderer>();
        r.sharedMaterial = glassMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Part(PrimitiveType.Sphere, root, new Vector3(0.006f, 0.122f, 0f), Vector3.one * 0.018f, oliveMaterial);
        var pick = Part(PrimitiveType.Cylinder, root, new Vector3(0.006f, 0.13f, 0f), new Vector3(0.003f, 0.03f, 0.003f), stoolMetalMaterial);
        pick.localRotation = Quaternion.Euler(0f, 0f, 25f);
        return root;
    }

    static Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var r = go.GetComponent<Renderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // Open cone, point down at the origin, rim at height h. Double-sided.
    static Mesh Cone(float radius, float h, int segments)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int side = 0; side < 2; side++)
        {
            int start = verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = (dir * h - Vector3.up * radius).normalized * (side == 0 ? 1f : -1f);
                verts.Add(Vector3.zero); normals.Add(n);
                verts.Add(dir * radius + Vector3.up * h); normals.Add(n);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2, b = a + 2;
                if (side == 0) { tris.Add(a); tris.Add(a + 1); tris.Add(b + 1); }
                else { tris.Add(a); tris.Add(b + 1); tris.Add(a + 1); }
            }
        }
        var mesh = new Mesh { name = "Martini Bowl" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------ helpers

    // The point on the floor in front of a slot machine, and the direction
    // from the machine toward its player.
    bool Front(SlotMachineBase machine, out Vector3 front, out Vector3 toPlayer)
    {
        front = toPlayer = default;
        if (machine == null || !machine.TryGetBetPanelPose(Vector3.zero, out Vector3 panel, out Quaternion facing)) return false;
        toPlayer = Flat(facing * Vector3.back);
        if (toPlayer.sqrMagnitude < 0.5f) return false;
        front = panel + toPlayer * 0.25f;
        front.y = Floor(front);
        return true;
    }

    // The hall floor under p (not the top of whatever p is next to).
    float Floor(Vector3 p)
    {
        Vector3 low = new Vector3(p.x, hall.center.y, p.z);
        if (NavMesh.SamplePosition(low, out var hit, 0.5f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - low.y) < 0.2f) return hit.position.y;
        if (Physics.Raycast(low + Vector3.up * 0.25f, Vector3.down, out var ray, 1f, ~0, QueryTriggerInteraction.Ignore)) return ray.point.y;
        return hall.center.y;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    static bool RendererBounds(Transform root, out Bounds b)
    {
        b = default;
        bool has = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has;
    }
}
