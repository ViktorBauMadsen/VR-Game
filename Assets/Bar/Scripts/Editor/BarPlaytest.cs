using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;

// Tools > Bar > Playtest: try the bar in Play mode without a headset.
// Stand At Bar moves the XR rig so the camera (and its AudioListener) is
// where a customer stands; the others press the bar's buttons / poke the
// bartender as if done with a controller.
public static class BarPlaytest
{
    const string Menu = "Tools/Bar/Playtest/";

    [MenuItem(Menu + "Stand At Bar")]
    static void StandAtBar()
    {
        var bartender = Object.FindAnyObjectByType<Bartender>();
        var cam = Camera.main;
        if (bartender == null || cam == null) { Debug.LogWarning("BarPlaytest: no bartender or main camera."); return; }
        Transform bar = bartender.transform.parent != null ? bartender.transform.parent : bartender.transform;
        Vector3 forward = Vector3.ProjectOnPlane(bar.forward, Vector3.up).normalized;
        Vector3 spot = bar.position + forward * 0.75f + Vector3.up * 1.6f;

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin != null)
        {
            origin.MoveCameraToWorldLocation(spot);
            origin.MatchOriginUpCameraForward(Vector3.up, -forward);
        }
        else
        {
            cam.transform.root.position += spot - cam.transform.position;
        }
        var balance = PlayerBalance.Instance;
        Debug.Log($"BarPlaytest: camera at {cam.transform.position}, {Vector3.Distance(cam.transform.position, bartender.transform.position + Vector3.up * 1.47f):0.00} m from the bartender's head. Balance: {(balance != null ? balance.Balance.ToString() : "none")}");
    }

    [MenuItem(Menu + "Poke Bartender")]
    static void Poke()
    {
        var bartender = Object.FindAnyObjectByType<Bartender>();
        if (bartender == null) return;
        typeof(Bartender).GetMethod("OnPoked", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(bartender, new object[] { null });
    }

    [MenuItem(Menu + "Buy Cigar")]
    static void BuyCigar() => Buy("cigar");

    [MenuItem(Menu + "Buy Whiskey")]
    static void BuyWhiskey() => Buy("whiskey");

    static void Buy(string item)
    {
        foreach (var button in Object.FindObjectsByType<BarOrderButton>())
        {
            var so = new SerializedObject(button);
            if (so.FindProperty("itemName").stringValue != item || so.FindProperty("menu").objectReferenceValue != null) continue;
            var bartender = (Bartender)so.FindProperty("bartender").objectReferenceValue;
            var prefab = (GameObject)so.FindProperty("itemPrefab").objectReferenceValue;
            int price = so.FindProperty("price").intValue;
            Vector3 euler = so.FindProperty("serveEuler").vector3Value;
            if (bartender != null) bartender.Order(prefab, item, price, Quaternion.Euler(euler), null);
            return;
        }
        Debug.LogWarning($"BarPlaytest: no '{item}' button found.");
    }

    [MenuItem(Menu + "Stand At Bar", true)]
    [MenuItem(Menu + "Poke Bartender", true)]
    [MenuItem(Menu + "Buy Cigar", true)]
    [MenuItem(Menu + "Buy Whiskey", true)]
    static bool InPlayMode() => Application.isPlaying;
}
