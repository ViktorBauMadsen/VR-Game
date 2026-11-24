using UnityEngine;

namespace Casino.Multiplayer
{
    // How one player looks to the others. Only the head and two hands are
    // sent over the network; everything else is worked out from those three
    // points, the same way the local player's own mirror body is:
    //   - the torso hangs under the head and turns toward where it faces
    //     (with a little lag, so looking around doesn't spin the whole body),
    //   - the arms are stretched from the shoulders to the hands,
    //   - the legs reach from the hips down to the floor.
    // Each player's jacket is their own colour, which is also the colour of
    // their chips on the roulette table.
    //
    // Their hands are copies of the rigged hand models the local player has
    // (see UpdateRiggedHands); the blocky hands are only a fallback.
    // The local player gets only the blocky hands (and only when the scene
    // has no rigged hand models), never a body.
    public sealed class SimpleAvatar : MonoBehaviour
    {
        const float ArmThickness = 0.08f, LegThickness = 0.13f;
        const float NeckDrop = 0.17f;       // head centre down to the neck
        const float TorsoLength = 0.56f;    // neck down to the hips

        Transform head, left, right;
        Transform torso, hipsBlock;
        readonly Transform[] upperArms = new Transform[2], forearms = new Transform[2], legs = new Transform[2], feet = new Transform[2];
        AvatarPose target;
        bool received, hasBody;
        Material material, skin, dark;
        float lastPose, yaw;

        // Height of the floor the players stand on (the local rig's is used -
        // everyone is in the same casino).
        public float FloorY { get; set; }

        public void Initialize(Material source, Color color, bool local)
        {
            material = new Material(source) { color = color };
            skin = new Material(source) { color = new Color(0.93f, 0.76f, 0.63f) };
            dark = new Material(source) { color = new Color(0.07f, 0.07f, 0.09f) };

            head = Part("Head", transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(.21f, .25f, .22f), local ? material : skin);
            if (!local)
            {
                Part("Eye L", head, PrimitiveType.Sphere, new Vector3(-0.2f, 0.08f, 0.43f), new Vector3(.13f, .13f, .1f), dark);
                Part("Eye R", head, PrimitiveType.Sphere, new Vector3(0.2f, 0.08f, 0.43f), new Vector3(.13f, .13f, .1f), dark);
                Part("Cap", head, PrimitiveType.Sphere, new Vector3(0f, 0.2f, -0.03f), new Vector3(1.04f, 0.75f, 1.04f), material);
                Part("Cap Peak", head, PrimitiveType.Cube, new Vector3(0f, 0.32f, 0.42f), new Vector3(0.75f, 0.06f, 0.5f), material);
            }
            else Part("Visor", head, PrimitiveType.Cube, new Vector3(0, 0, .45f), new Vector3(.85f, .35f, .2f), material);

            left = Hand("Left hand", -1, local ? material : skin);
            right = Hand("Right hand", 1, local ? material : skin);

            if (!local)
            {
                hasBody = true;
                torso = Part("Torso", transform, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.36f, TorsoLength * 0.5f, 0.23f), material);
                hipsBlock = Part("Hips", transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.33f, 0.2f, 0.23f), dark);
                for (int i = 0; i < 2; i++)
                {
                    string n = i == 0 ? "Right" : "Left";
                    upperArms[i] = Part(n + " Upper Arm", transform, PrimitiveType.Capsule, Vector3.zero, Vector3.one * ArmThickness, material);
                    forearms[i] = Part(n + " Forearm", transform, PrimitiveType.Capsule, Vector3.zero, Vector3.one * ArmThickness, material);
                    legs[i] = Part(n + " Leg", transform, PrimitiveType.Capsule, Vector3.zero, Vector3.one * LegThickness, dark);
                    feet[i] = Part(n + " Foot", transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.1f, 0.06f, 0.24f), dark);
                }
            }

            head.gameObject.SetActive(!local);
            if (!local) gameObject.SetActive(false);
        }

        Transform Hand(string label, float side, Material mat)
        {
            var root = new GameObject(label).transform;
            root.SetParent(transform, false);
            Part("Palm", root, PrimitiveType.Cube, Vector3.zero, new Vector3(.075f, .035f, .09f), mat);
            for (int i = 0; i < 4; i++)
                Part("Finger " + i, root, PrimitiveType.Capsule,
                    new Vector3((i - 1.5f) * .018f, 0, .06f), new Vector3(.015f, .028f, .015f), mat)
                    .localRotation = Quaternion.Euler(90, 0, 0);
            Part("Thumb", root, PrimitiveType.Capsule, new Vector3(-side * .043f, 0, .01f),
                new Vector3(.02f, .025f, .02f), mat).localRotation = Quaternion.Euler(65, side * 35, 0);
            return root;
        }

        Transform Part(string label, Transform parent, PrimitiveType shape, Vector3 position, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = label;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial = mat;
            return part.transform;
        }

        public void Apply(AvatarPose pose, bool immediate)
        {
            target = pose;
            lastPose = Time.unscaledTime;
            gameObject.SetActive(true);
            if (immediate || !received || Vector3.Distance(head.position, pose.HeadPosition) > 2f)
            {
                yaw = pose.HeadRotation.eulerAngles.y;
                Move(1);
            }
            received = true;
        }

        void Update()
        {
            if (received) Move(1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            if (received && Time.unscaledTime - lastPose > 3f) gameObject.SetActive(false);
        }

        void Move(float t)
        {
            Pose(head, target.HeadPosition, target.HeadRotation, t);
            Pose(left, target.LeftPosition, target.LeftRotation, t);
            Pose(right, target.RightPosition, target.RightRotation, t);
            if (hasBody)
            {
                UpdateRiggedHands();
                PoseBody();
            }
        }

        // ------------------------------------------------ rigged hands
        // Other players get the same rigged hand models you have (copied
        // from your own HandPoser hands), with their fingers following the
        // curls that player sends. The blocky hands stay only as a fallback
        // for scenes without hand models.

        readonly RemoteHand[] rigged = new RemoteHand[2]; // 0 = left, 1 = right
        float nextRiggedTry;

        void UpdateRiggedHands()
        {
            if ((rigged[0] == null || rigged[1] == null) && Time.unscaledTime >= nextRiggedTry)
            {
                nextRiggedTry = Time.unscaledTime + 1f;
                foreach (var source in FindObjectsByType<HandPoser>())
                {
                    int i = source.HandSide == HandPoser.Side.Left ? 0 : 1;
                    if (rigged[i] != null) continue;
                    Transform holder = i == 0 ? left : right;
                    var copy = source.CreateRemoteCopy(holder);
                    if (copy == null) continue;
                    rigged[i] = copy;
                    foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
                        if (!r.transform.IsChildOf(copy.transform)) r.enabled = false;
                }
            }
            if (rigged[0] != null)
            {
                rigged[0].SetCurls(AvatarPose.Curl(target.LeftIndex), AvatarPose.Curl(target.LeftGrip), AvatarPose.Curl(target.LeftThumb));
                rigged[0].SetSkin(skin.color);
            }
            if (rigged[1] != null)
            {
                rigged[1].SetCurls(AvatarPose.Curl(target.RightIndex), AvatarPose.Curl(target.RightGrip), AvatarPose.Curl(target.RightThumb));
                rigged[1].SetSkin(skin.color);
            }
        }

        // Where the arm should end: the rigged hand's wrist, else the controller.
        Vector3 HandAnchor(int i)
        {
            var hand = rigged[i == 0 ? 1 : 0]; // arm index 0 is the right arm
            if (hand != null && hand.Wrist != null) return hand.Wrist.position;
            return (i == 0 ? right : left).position;
        }

        // ------------------------------------------------ the player's own look

        PlayerAvatarBody.Rig rig;
        AvatarCustomizer customizer;
        GameObject custom;
        byte[] lastLook;

        // The other player's wardrobe choices arrived. The first time, the
        // blocky stand-in body is swapped for a copy of the real character
        // model (the same one you see of yourself in the mirror); after
        // that only its hat, hair, colours etc. are changed.
        public void SetLook(byte[] look)
        {
            if (!hasBody || look == null) return;
            if (lastLook != null && lastLook.Length == look.Length)
            {
                bool same = true;
                for (int i = 0; i < look.Length; i++) same &= lastLook[i] == look[i];
                if (same) return;
            }
            if (customizer == null && !BuildCustomBody()) return; // no character model in this scene
            lastLook = (byte[])look.Clone();
            customizer.SetSelections(look);
            skin.color = customizer.SkinColour; // the hands match
        }

        bool BuildCustomBody()
        {
            PlayerAvatarBody source = null;
            foreach (var candidate in FindObjectsByType<PlayerAvatarBody>())
            {
                var theirs = candidate.GetComponent<AvatarCustomizer>();
                if (theirs != null && !theirs.Remote && candidate.gameObject.activeInHierarchy) { source = candidate; break; }
            }
            if (source == null) return false;

            // Copied while switched off, so the copy's scripts don't start
            // up as "the local player" before they are told otherwise.
            source.gameObject.SetActive(false);
            custom = Instantiate(source.gameObject, transform);
            source.gameObject.SetActive(true);
            custom.name = "Character";

            var body = custom.GetComponent<PlayerAvatarBody>();
            customizer = custom.GetComponent<AvatarCustomizer>();
            if (body == null || customizer == null)
            {
                DestroyImmediate(custom);
                custom = null;
                customizer = null;
                return false;
            }
            rig = body.GetRig();
            DestroyImmediate(body); // this copy follows the network, not the headset
            customizer.PrepareRemote();
            // The original hides on a layer the player's own camera skips.
            foreach (var t in custom.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            custom.SetActive(true);

            // The stand-in body is no longer needed (the hands stay).
            foreach (var part in new[] { torso, hipsBlock, head })
                foreach (var r in part.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var group in new[] { upperArms, forearms, legs, feet })
                foreach (var part in group) part.gameObject.SetActive(false);
            return true;
        }

        void PoseCustomBody()
        {
            if (customizer != null) skin.color = customizer.SkinColour; // hands follow the skin tone
            Quaternion headRotation = head.rotation;
            rig.Head.SetPositionAndRotation(head.position - headRotation * rig.EyeOffset, headRotation);

            yaw = Mathf.MoveTowardsAngle(yaw, head.eulerAngles.y, 300f * Time.unscaledDeltaTime);
            Quaternion bodyRotation = Quaternion.Euler(0f, yaw, 0f);
            rig.Body.SetPositionAndRotation(rig.Head.position - Vector3.up * rig.NeckHeight, bodyRotation);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                if (rig.Shoulders[i] != null)
                    CharacterLimbs.PoseArm(rig.UpperArms[i], rig.Forearms[i], rig.Shoulders[i].position, HandAnchor(i), bodyRotation, side, rig.ArmThickness);

                if (rig.Hips[i] == null) continue;
                Vector3 hip = rig.Hips[i].position;
                Vector3 foot = new Vector3(hip.x, Mathf.Min(FloorY + 0.06f, hip.y - 0.1f), hip.z);
                CharacterLimbs.Stretch(rig.Legs[i], hip, foot, rig.LegThickness);
                if (rig.Feet[i] != null)
                    rig.Feet[i].SetPositionAndRotation(foot + bodyRotation * new Vector3(0f, -0.02f, 0.05f), bodyRotation);
            }
        }

        void PoseBody()
        {
            if (rig != null) { PoseCustomBody(); return; }
            // Turn toward where the head faces, but not instantly.
            yaw = Mathf.MoveTowardsAngle(yaw, head.eulerAngles.y, 300f * Time.unscaledDeltaTime);
            Quaternion bodyRotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 neck = head.position + Vector3.down * NeckDrop;
            Vector3 hips = neck + Vector3.down * TorsoLength;

            torso.SetPositionAndRotation((neck + hips) * 0.5f + Vector3.down * 0.02f, bodyRotation);
            hipsBlock.SetPositionAndRotation(hips, bodyRotation);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                Vector3 shoulder = neck + bodyRotation * new Vector3(side * 0.2f, -0.07f, 0f);
                CharacterLimbs.PoseArm(upperArms[i], forearms[i], shoulder, HandAnchor(i), bodyRotation, side, ArmThickness);

                Vector3 hip = hips + bodyRotation * new Vector3(side * 0.09f, 0f, 0f);
                Vector3 foot = new Vector3(hip.x, Mathf.Min(FloorY + 0.06f, hip.y - 0.1f), hip.z);
                CharacterLimbs.Stretch(legs[i], hip, foot, LegThickness);
                feet[i].SetPositionAndRotation(foot + bodyRotation * new Vector3(0f, -0.02f, 0.05f), bodyRotation);
            }
        }

        static void Pose(Transform part, Vector3 position, Quaternion rotation, float t)
        {
            part.position = Vector3.Lerp(part.position, position, t);
            part.rotation = Quaternion.Slerp(part.rotation, rotation, t);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (skin != null) Destroy(skin);
            if (dark != null) Destroy(dark);
        }
    }
}
