// Conic gradient: the colour at each pixel is the stop list evaluated at the pixel's angle around the centre, measured clockwise from
// straight up (as CSS does). Stops interpolate as straight (non-premultiplied) RGBA, as the wedge fallback does, and the result is premultiplied.
#define D2D_INPUT_COUNT 0
#define D2D_REQUIRES_SCENE_POSITION
#include "d2d1effecthelpers.hlsli"

cbuffer constants : register(b0)
{
    float2 center;   // where the angle is measured from, in scene pixels
    float start;     // the first stop's angle, in radians
    float count;     // how many of the stops below are used (2 to 16)
    float4 positions[4];  // the stops' angles in radians, four to a vector
    float4 colors[16];    // the stops' straight RGBA
};

D2D_PS_ENTRY(main)
{
    float2 p = D2DGetScenePosition().xy - center;
    float raw = atan2(p.x, -p.y);
    float turn = 6.28318530718;
    float rel = raw - start;
    rel = rel - turn * floor(rel / turn);
    float angle = start + rel;

    float4 result = colors[0];
    [unroll]
    for (int i = 1; i < 16; i++)
    {
        if (i < count)
        {
            float before = positions[(i - 1) / 4][(i - 1) % 4];
            float after = positions[i / 4][i % 4];
            float t = saturate((angle - before) / max(after - before, 1e-6));
            float4 mixed = lerp(colors[i - 1], colors[i], t);
            result = angle > before ? mixed : result;
        }
    }

    return float4(result.rgb * result.a, result.a);
}
