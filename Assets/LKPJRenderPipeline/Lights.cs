using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;


public class Lights
{
    [GenerateHLSL(PackingRules.Exact, false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData
    {
        public Vector3 position;

        // Octahedral encoded direction:
        // low16  = X UNORM16
        // high16 = Y UNORM16
        public uint direction;

        // low16  = R FP16
        // high16 = G FP16
        public uint colorRG;

        // low16  = B FP16
        // high16 = range UNORM16
        public uint colorBRange;

        // low16  = cos(inner / 2) UNORM16
        // high16 = cos(outer / 2) UNORM16
        public uint spotCosines;

        // low2   = type
        // high30 = unique ID
        public uint idAndType;
    }
    private static readonly int lightStructStride = UnsafeUtility.SizeOf<LightData>();
    private int maxLights;
    private Light[] lights;
    private Light[] lightArray;


    public Lights(LKPJRenderPipelineAsset asset)
    {
        maxLights = asset.maxLights;
        lightArray = new Light[maxLights];
    }

    void updateLights(ScriptableRenderContext context)
    {
        lights = Object.FindObjectsByType<Light>();
    }
}