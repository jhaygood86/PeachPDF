using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Benchmarks;

/// <summary>
/// The raster filter kernels on a 1024x768 surface of non-trivial content: the Gaussian blur at a small and a large sigma, the drop shadow, a
/// colour matrix, morphology at a small and a large radius, and feTurbulence. Each in-place filter works on a fresh copy of the source so the
/// content it sees does not drift from one invocation to the next (the copy is part of the measured time and is the same for every build).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class FilterBenchmarks
{
    private const int Width = 1024;
    private const int Height = 768;

    private RasterSurface _source = null!;
    private RasterSurface _work = null!;
    private RasterSurface _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new RasterSurface(Width, Height, 0, 0, 1, 1);
        _work = new RasterSurface(Width, Height, 0, 0, 1, 1);
        _result = new RasterSurface(Width, Height, 0, 0, 1, 1);

        // Premultiplied RGBA with gradients, hard edges and transparent areas, from a fixed pattern (no randomness: the content is the same every run).
        for (int y = 0; y < Height; y++)
        {
            var row = _source.Row(y);
            for (int x = 0; x < Width; x++)
            {
                bool inside = (x / 64 + y / 64) % 3 != 0;
                int a = inside ? 128 + (x + y) % 128 : 0;
                row[x * 4 + 0] = (byte)(x * a / Width);
                row[x * 4 + 1] = (byte)(y * a / Height);
                row[x * 4 + 2] = (byte)(a / 2);
                row[x * 4 + 3] = (byte)a;
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Dispose();
        _work.Dispose();
        _result.Dispose();
    }

    private void ResetWork()
    {
        for (int y = 0; y < Height; y++)
            _source.Row(y).CopyTo(_work.Row(y));
    }

    /// <summary>Gaussian blur at sigma 1.5 (the exact-kernel path).</summary>
    [Benchmark]
    public void BlurSmall()
    {
        ResetWork();
        GaussianBlur.Apply(_work, 1.5, 1.5);
    }

    /// <summary>Gaussian blur at sigma 12 (the three-box path).</summary>
    [Benchmark]
    public void BlurLarge()
    {
        ResetWork();
        GaussianBlur.Apply(_work, 12, 12);
    }

    /// <summary>A drop shadow offset by 8 px with a sigma of 6.</summary>
    [Benchmark]
    public void ShadowDrop()
    {
        ResetWork();
        DropShadow.Apply(_work, 8, 8, 6, 6, 0, 0, 0, 160);
    }

    /// <summary>A grayscale colour matrix applied in place.</summary>
    [Benchmark]
    public void ColorMatrixGray()
    {
        ResetWork();
        var gray = new ColorMatrix(
            new Matrix4x4(0.2126f, 0.2126f, 0.2126f, 0, 0.7152f, 0.7152f, 0.7152f, 0, 0.0722f, 0.0722f, 0.0722f, 0, 0, 0, 0, 1),
            Vector4.Zero);
        ColorMatrixFilter.ApplyInPlace(_work, gray);
    }

    /// <summary>The linearRGB and sRGB conversions every default SVG filter pays.</summary>
    [Benchmark]
    public void ColorSpaceRoundTrip()
    {
        ResetWork();
        FilterOps.ConvertColorSpace(_work, toLinear: true);
        FilterOps.ConvertColorSpace(_work, toLinear: false);
    }

    /// <summary>Dilation by a 3 px radius.</summary>
    [Benchmark]
    public void MorphologySmall() => FilterOps.Morphology(_source, _result, 3, 3, dilate: true);

    /// <summary>Dilation by a 12 px radius.</summary>
    [Benchmark]
    public void MorphologyLarge() => FilterOps.Morphology(_source, _result, 12, 12, dilate: true);

    /// <summary>Four-octave fractal turbulence over the whole surface.</summary>
    [Benchmark]
    public void TurbulenceFractal() =>
        new Turbulence(1).Render(_result, 1, 1, 0.02, 0.02, 4, fractalSum: true, stitching: false, 0, 0, Width, Height);

    /// <summary>Arithmetic compositing of two surfaces.</summary>
    [Benchmark]
    public void ArithmeticComposite() => FilterOps.Arithmetic(_source, _source, _result, 0, 0.5, 0.5, 0);
}
