using Unity.Netcode;
using UnityEngine;

namespace Casino.Multiplayer
{
    // World-space poses include both room-scale movement and XR Origin locomotion.
    public struct AvatarPose : INetworkSerializable
    {
        public Vector3 HeadPosition, LeftPosition, RightPosition;
        public Quaternion HeadRotation, LeftRotation, RightRotation;
        // Finger curls, 0 (open) .. 255 (closed): index, grip (middle/ring/pinky), thumb.
        public byte LeftIndex, LeftGrip, LeftThumb, RightIndex, RightGrip, RightThumb;

        public const int Bytes = 3 * (12 + 16) + 6;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref HeadPosition);
            serializer.SerializeValue(ref HeadRotation);
            serializer.SerializeValue(ref LeftPosition);
            serializer.SerializeValue(ref LeftRotation);
            serializer.SerializeValue(ref RightPosition);
            serializer.SerializeValue(ref RightRotation);
            serializer.SerializeValue(ref LeftIndex);
            serializer.SerializeValue(ref LeftGrip);
            serializer.SerializeValue(ref LeftThumb);
            serializer.SerializeValue(ref RightIndex);
            serializer.SerializeValue(ref RightGrip);
            serializer.SerializeValue(ref RightThumb);
        }

        public static byte Curl(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        public static float Curl(byte value) => value / 255f;

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
