//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef LIGHTS_CS_HLSL
#define LIGHTS_CS_HLSL
// Generated from Lights+PackedLightData
// PackingRules = Exact
struct PackedLightData
{
    float3 position;
    uint direction;
    uint colorRG;
    uint colorBRange;
    uint spotCosines;
    uint idAndType;
};


#endif
