# Shaders

`LcdBlend.hlsl` is the pixel shader that blends text over the pixels behind it on the GPU, one colour channel at a time (ClearType). `LcdBlend.bin` is its
compiled form, embedded in the assembly; rebuild it after changing the source (the Windows SDK's `fxc`):

```
fxc -nologo -T ps_4_0_level_9_1 -D D2D_FULL_SHADER -D D2D_ENTRY=main -E main -I "<Windows Kits>\Include\<version>\um" -Fo LcdBlend.bin LcdBlend.hlsl
```
