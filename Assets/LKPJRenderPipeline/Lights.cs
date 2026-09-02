using UnityEngine;
using UnityEngine.Rendering;

public class Lights
{
    [GenerateHLSL(PackingRules.Exact, false)]
    struct Light
    {
        public Vector4 positionRange;
        public Vector2 direction;
    };
}