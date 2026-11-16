using UnityEngine;

namespace Casino.Multiplayer
{
    public sealed class SimpleAvatar : MonoBehaviour
    {
        Transform head, left, right;
        AvatarPose target;
        bool received;
        Material material;
        float lastPose;

        public void Initialize(Material source, Color color, bool local)
        {
            material = new Material(source) { color = color };
            head = Part("Head", transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(.21f, .25f, .22f));
            Part("Visor", head, PrimitiveType.Cube, new Vector3(0, 0, .45f), new Vector3(.85f, .35f, .2f));
            left = Hand("Left hand", -1);
            right = Hand("Right hand", 1);
            head.gameObject.SetActive(!local);
            if (!local) gameObject.SetActive(false);
        }

        Transform Hand(string label, float side)
        {
            var root = new GameObject(label).transform;
            root.SetParent(transform, false);
            Part("Palm", root, PrimitiveType.Cube, Vector3.zero, new Vector3(.075f, .035f, .09f));
            for (int i = 0; i < 4; i++)
                Part("Finger " + i, root, PrimitiveType.Capsule,
                    new Vector3((i - 1.5f) * .018f, 0, .06f), new Vector3(.015f, .028f, .015f))
                    .localRotation = Quaternion.Euler(90, 0, 0);
            Part("Thumb", root, PrimitiveType.Capsule, new Vector3(-side * .043f, 0, .01f),
                new Vector3(.02f, .025f, .02f)).localRotation = Quaternion.Euler(65, side * 35, 0);
            return root;
        }

        Transform Part(string label, Transform parent, PrimitiveType shape, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = label;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }

        public void Apply(AvatarPose pose, bool immediate)
        {
            target = pose;
            lastPose = Time.unscaledTime;
            gameObject.SetActive(true);
            if (immediate || !received || Vector3.Distance(head.position, pose.HeadPosition) > 2f)
                Move(1);
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
        }

        static void Pose(Transform part, Vector3 position, Quaternion rotation, float t)
        {
            part.position = Vector3.Lerp(part.position, position, t);
            part.rotation = Quaternion.Slerp(part.rotation, rotation, t);
        }

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
