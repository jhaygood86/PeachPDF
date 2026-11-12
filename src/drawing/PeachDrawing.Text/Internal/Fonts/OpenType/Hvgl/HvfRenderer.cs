using System;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Hvgl
{
    /// <summary>
    /// Draws the glyphs of a font with an <c>hvgl</c> table by calling Apple's HVF library (<c>/usr/lib/libhvf.dylib</c>, macOS 15.4 and
    /// iOS 18.4 and later), the way FreeType's HVF driver does. The library blends the axes and places the parts of a composite; this
    /// class owns the aligned native memory the library works in and turns what it draws into a <see cref="GlyphOutline"/>.
    /// </summary>
    /// <remarks>
    /// A renderer holds state about the part it was last set to, so one is used by one thread at a time (<see cref="TryRender"/>
    /// takes a lock). Excluded from code coverage like the other platform-specific native lookups: the managed
    /// side of the conversion is <see cref="HvfOutlineBuilder"/>, which is tested on every host.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    internal sealed unsafe partial class HvfRenderer : IDisposable
    {
        private const string Library = "/usr/lib/libhvf.dylib";

        /// <summary>How many glyphs are drawn before the library's cache of loaded parts is emptied (its documentation suggests about a dozen).</summary>
        private const int GlyphsPerCacheClear = 16;

        // The tables are copied here because the library wants them on an eight byte boundary and a table sits wherever the font put it.
        private readonly void* _hvgl;
        private readonly void* _hvpm;
        private readonly void* _renderer;
        private readonly uint _glyphCount;
        private readonly Lock _gate = new();
        private int _glyphsSinceClear;
        private bool _disposed;

        private HvfRenderer(void* hvgl, void* hvpm, void* renderer, uint glyphCount)
        {
            _hvgl = hvgl;
            _hvpm = hvpm;
            _renderer = renderer;
            _glyphCount = glyphCount;
        }

        /// <summary>Whether this process can call the library: an Apple platform new enough to ship it, with the library loadable.</summary>
        internal static bool IsAvailable { get; } = ProbeAvailability();

        private static bool ProbeAvailability()
        {
            var supported = OperatingSystem.IsMacOS() ? OperatingSystem.IsMacOSVersionAtLeast(15, 4)
                : OperatingSystem.IsIOS() ? OperatingSystem.IsIOSVersionAtLeast(18, 4)
                : OperatingSystem.IsMacCatalyst() ? OperatingSystem.IsMacCatalystVersionAtLeast(18, 4)
                : OperatingSystem.IsTvOS() && OperatingSystem.IsTvOSVersionAtLeast(18, 4);

            if (!supported || !NativeLibrary.TryLoad(Library, out var handle))
                return false;

            // The functions are looked up once so that a library missing one of them is "not available" rather than a failure at the first glyph.
            foreach (var name in new[] { "HVF_part_renderer_storage_size", "HVF_open_part_renderer", "HVF_set_render_part", "HVF_set_axis_value", "HVF_render_current_part", "HVF_close_part_renderer", "HVF_glyph_count", "HVF_clear_part_cache" })
            {
                if (!NativeLibrary.TryGetExport(handle, name, out _))
                    return false;
            }

            return true;
        }

        /// <summary>A renderer for the <c>hvgl</c> (and <c>hvpm</c>) table of <paramref name="face"/>, or null when it has none or the library is not available.</summary>
        internal static HvfRenderer? TryCreateFor(OpenTypeFontface face)
        {
            if (!IsAvailable || !face.TableDictionary.TryGetValue(TableTagNames.Hvgl, out var hvgl))
                return null;

            var bytes = face.FontSource.Bytes;
            var hvpm = face.TableDictionary.TryGetValue(TableTagNames.Hvpm, out var map)
                ? bytes.AsSpan(map.Offset, map.Length)
                : default;
            return TryCreate(bytes.AsSpan(hvgl.Offset, hvgl.Length), hvpm);
        }

        /// <summary>A renderer over the given tables, or null when the library is not available or does not accept them.</summary>
        internal static HvfRenderer? TryCreate(ReadOnlySpan<byte> hvgl, ReadOnlySpan<byte> hvpm)
        {
            if (!IsAvailable || hvgl.IsEmpty)
                return null;

            var storageSize = HVF_part_renderer_storage_size();
            var hvglCopy = Allocate(hvgl.Length);
            var hvpmCopy = hvpm.IsEmpty ? null : Allocate(hvpm.Length);
            var storage = Allocate((int)storageSize);

            hvgl.CopyTo(new Span<byte>(hvglCopy, hvgl.Length));
            if (hvpmCopy is not null)
                hvpm.CopyTo(new Span<byte>(hvpmCopy, hvpm.Length));

            if (HVF_open_part_renderer(hvglCopy, (nuint)hvgl.Length, hvpmCopy, (nuint)hvpm.Length, storage, storageSize) != 0)
            {
                Free(hvglCopy);
                Free(hvpmCopy);
                Free(storage);
                return null;
            }

            return new HvfRenderer(hvglCopy, hvpmCopy, storage, HVF_glyph_count(storage));
        }

        private static void* Allocate(int size)
        {
            var memory = NativeMemory.AlignedAlloc((nuint)Math.Max(size, 8), 8);
            NativeMemory.Clear(memory, (nuint)Math.Max(size, 8));
            return memory;
        }

        private static void Free(void* memory)
        {
            if (memory is not null)
                NativeMemory.AlignedFree(memory);
        }

        /// <summary>How many glyphs the font has (the parts that can be drawn on their own; glyph numbers are part numbers).</summary>
        internal int GlyphCount => (int)_glyphCount;

        /// <summary>
        /// Draws glyph <paramref name="glyphIndex"/> with each axis at the given normalized value (-1 to 1, in the order of the <c>fvar</c>
        /// axes; none for the default location). False for a glyph the font does not have, one with no outline (a space), or when the
        /// library reports a failure.
        /// </summary>
        internal bool TryRender(int glyphIndex, ReadOnlySpan<double> axes, out GlyphOutline outline)
        {
            outline = new GlyphOutline();
            if (glyphIndex < 0 || (uint)glyphIndex >= _glyphCount)
                return false;

            var builder = new HvfOutlineBuilder();
            var handle = GCHandle.Alloc(builder);
            try
            {
                lock (_gate)
                {
                    if (_disposed)
                        return false;

                    if (++_glyphsSinceClear >= GlyphsPerCacheClear)
                    {
                        _glyphsSinceClear = 0;
                        HVF_clear_part_cache(_renderer);
                    }

                    // Setting the part puts every axis back to zero, so the axes are set after it.
                    if (HVF_set_render_part(_renderer, (nuint)glyphIndex) != 0)
                        return false;

                    for (var axis = 0; axis < axes.Length; axis++)
                    {
                        // An axis the part does not use is refused; that is not a reason to draw nothing.
                        if (axes[axis] != 0)
                            _ = HVF_set_axis_value(_renderer, axis, Math.Clamp(axes[axis], -1.0, 1.0));
                    }

                    if (HVF_render_current_part(_renderer, &OnInstruction, (void*)GCHandle.ToIntPtr(handle)) != 0)
                        return false;
                }
            }
            finally
            {
                handle.Free();
            }

            outline = builder.Finish();
            return !outline.IsEmpty;
        }

        /// <summary>
        /// The library's callback. The payload of an instruction is a union read only as far as the instruction carries: a point is two
        /// doubles, a quadratic curve four, a cubic curve six, and the instructions that begin or end a path or a part carry none at all
        /// (the pointer is null for most of them).
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static uint OnInstruction(uint instruction, double* parameters, void* caller)
        {
            try
            {
                var builder = (HvfOutlineBuilder)GCHandle.FromIntPtr((nint)caller).Target!;
                var kind = (HvfOutlineBuilder.Instruction)instruction;

                return (uint)(kind switch
                {
                    HvfOutlineBuilder.Instruction.AddPoint or HvfOutlineBuilder.Instruction.AddLine when parameters is not null
                        => builder.Handle(kind, parameters[0], parameters[1]),
                    HvfOutlineBuilder.Instruction.AddQuad when parameters is not null
                        => builder.Handle(kind, parameters[0], parameters[1], parameters[2], parameters[3]),
                    HvfOutlineBuilder.Instruction.AddCubic when parameters is not null
                        => builder.Handle(kind, parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5]),
                    _ => builder.Handle(kind)
                });
            }
            catch
            {
                // An exception must not unwind into native frames.
                return (uint)HvfOutlineBuilder.Action.Stop;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                HVF_close_part_renderer(_renderer);
                Free(_renderer);
                Free(_hvgl);
                Free(_hvpm);
            }

            GC.SuppressFinalize(this);
        }

        ~HvfRenderer() => Dispose();

        [LibraryImport(Library)]
        private static partial nuint HVF_part_renderer_storage_size();

        [LibraryImport(Library)]
        private static partial int HVF_open_part_renderer(void* hvgl, nuint hvglSize, void* hvpm, nuint hvpmSize, void* storage, nuint storageSize);

        [LibraryImport(Library)]
        private static partial uint HVF_glyph_count(void* renderer);

        [LibraryImport(Library)]
        private static partial int HVF_set_render_part(void* renderer, nuint part);

        [LibraryImport(Library)]
        private static partial int HVF_set_axis_value(void* renderer, int axis, double value);

        [LibraryImport(Library)]
        private static partial int HVF_render_current_part(void* renderer, delegate* unmanaged[Cdecl]<uint, double*, void*, uint> context, void* callerData);

        [LibraryImport(Library)]
        private static partial int HVF_clear_part_cache(void* renderer);

        [LibraryImport(Library)]
        private static partial int HVF_close_part_renderer(void* renderer);
    }
}
