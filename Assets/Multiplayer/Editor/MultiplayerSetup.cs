using System;
using System.IO;
using Casino.Multiplayer;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

public static class MultiplayerSetup
{
    [MenuItem("Tools/Multiplayer/Prepare and Validate")]
    public static void PrepareAndValidate()
    {
        const string path = "Assets/Resources/Multiplayer/Avatar.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
        {
            Directory.CreateDirectory("Assets/Resources/Multiplayer");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
            AssetDatabase.CreateAsset(new Material(shader), path);
        }
        PlayerSettings.Android.forceInternetPermission = true;
        AssetDatabase.SaveAssets();
        ValidatePose();
        Debug.Log("MULTIPLAYER_VALIDATION_PASSED: material, Android Internet permission, pose serialization and invalid pose rejection. Open SampleScene and press Play.");
    }

    static void ValidatePose()
    {
        var source = new AvatarPose
        {
            HeadPosition = new Vector3(1, 1.7f, 3), HeadRotation = Quaternion.Euler(0, 45, 0),
            LeftPosition = new Vector3(.8f, 1, 3), LeftRotation = Quaternion.Euler(30, 0, 20),
            RightPosition = new Vector3(1.2f, 1, 3), RightRotation = Quaternion.identity
        };
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        // NamedMessage consumes an eight-byte hash before invoking our handler.
        writer.WriteValueSafe(123UL);
        writer.WriteNetworkSerializable(source);
        if (writer.Length != 92) throw new Exception("Unexpected pose wire size: " + writer.Length);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadValueSafe(out ulong messageHash);
        if (messageHash != 123UL || reader.Length - reader.Position != 84)
            throw new Exception("Named message payload offset failed.");
        reader.ReadNetworkSerializable(out AvatarPose result);
        if (!result.IsValid || result.HeadPosition != source.HeadPosition || result.LeftPosition != source.LeftPosition
            || result.RightPosition != source.RightPosition || Quaternion.Angle(result.HeadRotation, source.HeadRotation) > .01f
            || Quaternion.Angle(result.LeftRotation, source.LeftRotation) > .01f || result.RightRotation != source.RightRotation)
            throw new Exception("Pose round trip failed.");
        result.HeadPosition.x = float.NaN;
        if (result.IsValid) throw new Exception("NaN pose accepted.");
        result = source;
        result.LeftRotation = new Quaternion(0, 0, 0, 0);
        if (result.IsValid) throw new Exception("Invalid rotation accepted.");
    }
}
