// Per-channel (ClearType) blend: out = backdrop * (1 - alpha) + inkTimesAlpha, with the three stripes' alphas in the colour channels.
#define D2D_INPUT_COUNT 3
#define D2D_INPUT0_SIMPLE
#define D2D_INPUT1_SIMPLE
#define D2D_INPUT2_SIMPLE
#include "d2d1effecthelpers.hlsli"

D2D_PS_ENTRY(main)
{
    float4 backdrop = D2DGetInput(0);
    float4 ink = D2DGetInput(1);
    float4 alpha = D2DGetInput(2);
    return float4(backdrop.rgb * (1.0 - alpha.rgb) + ink.rgb, 1.0);
}
