using UnityEngine;

// Tags a single wheel section with the number painted on it, so
// RouletteWheel can read a spin's result straight from the section the ball
// actually settled in instead of trusting a separate angle formula to stay
// in sync with the visuals. Built by SetupRouletteWheel (or wired up from
// an artist-made model) - one per pocket.
public class RoulettePocket : MonoBehaviour
{
    public int Number;

    Renderer _renderer;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }

    // Where this pocket actually is in the world. Some imported models
    // (e.g. an FBX exported with "freeze transform" applied) bake every
    // node's offset into its mesh vertices and leave the Transform itself
    // sitting at the parent's origin - transform.position would then report
    // the same point for every pocket. The renderer's bounds always reflect
    // the real mesh position regardless of which one holds the offset, so
    // it's the reliable choice here.
    public Vector3 WorldCenter
    {
        get
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            return _renderer != null ? _renderer.bounds.center : transform.position;
        }
    }
}
