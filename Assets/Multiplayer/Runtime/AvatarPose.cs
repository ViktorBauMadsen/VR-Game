using Unity.Netcode;
using UnityEngine;

namespace Casino.Multiplayer
{
    // World-space poses include both room-scale movement and XR Origin locomotion.
    public struct AvatarPose : INetworkSerializable
    {
        public Vector3 HeadPosition, LeftPosition, RightPosition;
        public Quaternion HeadRotation, LeftRotation, RightRotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref HeadPosition);
            serializer.SerializeValue(ref HeadRotation);
            serializer.SerializeValue(ref LeftPosition);
            serializer.SerializeValue(ref LeftRotation);
            serializer.SerializeValue(ref RightPosition);
            serializer.SerializeValue(ref RightRotation);
        }

        public bool IsValid => Valid(HeadPosition) && Valid(LeftPosition) && Valid(RightPosition)
            && Valid(HeadRotation) && Valid(LeftRotation) && Valid(RightRotation);

        static bool Valid(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && v.sqrMagnitude < 100000000f;
        static bool Valid(Quaternion q)
        {
            float length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return Finite(length) && Mathf.Abs(length - 1f) < 0.1f;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
