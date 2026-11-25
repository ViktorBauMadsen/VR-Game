using System.Collections.Generic;
using UnityEngine;

// Sits on the "Merged Decor" object made by Tools > Casino > Merge Decor and
// remembers which original renderers it switched off, so Tools > Casino >
// Unmerge Decor can switch them back on. Does nothing at runtime.
public sealed class MergedDecor : MonoBehaviour
{
    public List<Renderer> sources = new List<Renderer>();
}
