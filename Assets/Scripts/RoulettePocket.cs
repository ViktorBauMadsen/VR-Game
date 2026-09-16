using UnityEngine;

// Tags a single wheel section with the number painted on it, so
// RouletteWheel can read a spin's result straight from the section the ball
// actually settled in instead of trusting a separate angle formula to stay
// in sync with the visuals. Built by SetupRouletteWheel, one per pocket.
public class RoulettePocket : MonoBehaviour
{
    public int Number;
}
