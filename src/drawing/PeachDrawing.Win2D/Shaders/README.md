# Shaders

`LcdBlend.hlsl` is the pixel shader that blends text over the pixels behind it on the GPU, one color channel at a time (ClearType). `LcdBlend.bin` is its
compiled form, embedded in the assembly; rebuild it after changing the source (the Windows SDK's `fxc`):

```
fxc -nologo -T ps_4_0_level_9_1 -D D2D_FULL_SHADER -D D2D_ENTRY=main -E main -I "<Windows Kits>\Include\<version>\um" -Fo LcdBlend.bin LcdBlend.hlsl
```

`ConicGradient.hlsl` paints a conic gradient (Win2D has no brush for one): the color at each pixel is the stop list evaluated at its angle around the center, for up to 16 stops (a gradient with more is drawn as wedges). Its constants are the effect's properties (`center`, `start`, `count`, `positions`, `colors`). It is compiled for `ps_4_0`, because the stop loop does not fit the level 9 instruction limit, so a device without it falls back to the wedges:

```
fxc -nologo -T ps_4_0 -D D2D_FULL_SHADER -D D2D_ENTRY=main -E main -I "<Windows Kits>\Include\<version>\um" -Fo ConicGradient.bin ConicGradient.hlsl
```
