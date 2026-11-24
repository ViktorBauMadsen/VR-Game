using UnityEngine;

// The ithappy characters are a body plus separate clothing meshes, each with
// its own culling box sized for the standing bind pose. Once a guest sits or
// leans, the clothes' boxes no longer cover the deformed mesh, so at some
// camera angles Unity culls the jacket/pants/shoes while the body stays -
// the NPC "undresses". Give every skinned part one generous box around the
// whole character instead (cheaper than updateWhenOffscreen, which recomputes
// bounds from the bones every frame).
public static class SkinnedBounds
{
    public static void Encompass(Transform character, float height = 2f, float radius = 1.1f)
    {
        Vector3 worldCenter = character.position + character.up * (height * 0.5f);
        float worldSize = Mathf.Max(height, radius * 2f) + 0.4f;

        foreach (var smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform space = smr.rootBone != null ? smr.rootBone : smr.transform;
            Vector3 s = space.lossyScale;
            float scale = Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            if (scale < 1e-6f) continue;
            // A cube, so the bone's rotation doesn't matter; the root bone moves
            // with the animation, so the box is a bit bigger than the character.
            smr.localBounds = new Bounds(space.InverseTransformPoint(worldCenter), Vector3.one * (worldSize * 1.5f / scale));
            smr.updateWhenOffscreen = false;
        }
    }
}
