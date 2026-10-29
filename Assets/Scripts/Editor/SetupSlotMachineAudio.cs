using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupSlotMachineAudio
{
    const string LeverPullClipPath = "Assets/Audio/Sound Effects/Lever Pull.mp3";

    [MenuItem("Tools/Setup Slot Machine Audio")]
    static void Setup()
    {
        var handleObj = GameObject.Find("Handle");
        if (handleObj == null)
        {
            Debug.LogError("SetupSlotMachineAudio: couldn't find a GameObject named 'Handle' in the open scene.");
            return;
        }
        var lever = handleObj.GetComponent<SlotMachineLever>();
        if (lever == null)
        {
            Debug.LogError("SetupSlotMachineAudio: 'Handle' has no SlotMachineLever component.");
            return;
        }

        var slotMachineObj = GameObject.Find("SlotMachine");
        if (slotMachineObj == null)
        {
            Debug.LogError("SetupSlotMachineAudio: couldn't find a GameObject named 'SlotMachine' in the open scene.");
            return;
        }
        var slotMachine = slotMachineObj.GetComponent<SlotMachine>();
        if (slotMachine == null)
        {
            Debug.LogError("SetupSlotMachineAudio: 'SlotMachine' has no SlotMachine component.");
            return;
        }

        var leverPullClip = AssetDatabase.LoadAssetAtPath<AudioClip>(LeverPullClipPath);
        if (leverPullClip == null)
        {
            Debug.LogError($"SetupSlotMachineAudio: couldn't load clip at '{LeverPullClipPath}'.");
            return;
        }
        // The old spin-start sound is no longer wired here - it was timed
        // for the previous short spin. Reel ticks/stops/wins are handled by
        // SlotMachine itself.

        var leverAudioSource = GetOrAddAudioSource(handleObj);
        var machineAudioSource = GetOrAddAudioSource(slotMachineObj);

        var leverSo = new SerializedObject(lever);
        leverSo.FindProperty("audioSource").objectReferenceValue = leverAudioSource;
        leverSo.FindProperty("pullClip").objectReferenceValue = leverPullClip;
        leverSo.ApplyModifiedProperties();

        var slotMachineSo = new SerializedObject(slotMachine);
        slotMachineSo.FindProperty("audioSource").objectReferenceValue = machineAudioSource;
        slotMachineSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("SlotMachine audio set up: 'Lever Pull' wired to the Handle's AudioSource, and the SlotMachine's AudioSource assigned. Save the scene to keep it.");
    }

    static AudioSource GetOrAddAudioSource(GameObject obj)
    {
        var source = obj.GetComponent<AudioSource>();
        if (source == null) source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f; // 3D sound - makes sense coming from a physical machine in VR
        return source;
    }
}
