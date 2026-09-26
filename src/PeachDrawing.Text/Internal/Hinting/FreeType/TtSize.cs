/****************************************************************************
 *
 * ttobjs.c
 *
 *   Objects manager (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

/****************************************************************************
 *
 * ttobjs.h
 *
 *   Objects manager (specification).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

/****************************************************************************
 *
 * ftobjs.c
 *
 *   The FreeType private base classes (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttobjs.c, ttobjs.h, ftobjs.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;
using System.Linq;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// What the font's <c>fpgm</c> and <c>prep</c> programs leave behind for one size of one face in one mode: FreeType's
/// <c>TT_Size</c> after <c>tt_size_init_bytecode</c> and <c>tt_size_run_prep</c> (the scaled control values, the storage
/// area, the graphics state, the twilight zone and the function and instruction definitions), plus the decisions
/// <c>tt_loader_init</c> takes from them. Immutable once built: every glyph is hinted from a copy of it, so a size can be
/// shared between threads and cached.
/// </summary>
internal sealed class TtSize
{
    public TtFace Face { get; }

    /// <summary>The requested size in 26.6 pixels per em.</summary>
    public int Ppem26Dot6 { get; }

    public TtInterpreterVersion Version { get; }
    public TtRenderMode Mode { get; }

    /// <summary>The scale from font units to 26.6 pixels, in 16.16 (<c>metrics->x_scale</c>, which is also the y scale: pixels are square).</summary>
    public int Scale => Metrics.XScale;

    public TtSizeMetrics Metrics;

    /// <summary>The point size in 26.6 for the <c>MPS</c> instruction.</summary>
    public int PointSize { get; private set; }

    /// <summary>The scaled control values (after the CVT program).</summary>
    public int[] Cvt { get; private set; } = [];

    /// <summary>The storage area after the CVT program (which starts from zero).</summary>
    public int[] Storage { get; private set; } = [];

    /// <summary>The graphics state glyph programs start from.</summary>
    public TtGraphicsState GraphicsState;

    /// <summary>The twilight zone the CVT program left (its points are copied for each glyph).</summary>
    public TtGlyphZone Twilight { get; } = new();

    public TtDefRecord[] FDefs { get; private set; } = [];
    public int NumFDefs { get; private set; }
    public TtDefRecord[] IDefs { get; private set; } = [];
    public int NumIDefs { get; private set; }
    public uint MaxFunc { get; private set; }
    public uint MaxIns { get; private set; }

    /// <summary>
    /// Whether the CVT program turned hinting off for this size (<c>INSTCTRL</c>, selector 1): glyphs are then loaded scaled
    /// but not hinted.
    /// </summary>
    public bool HintingDisabled { get; private set; }

    /// <summary>
    /// The state of backward compatibility (<c>exec->backward_compatibility</c>) a glyph starts in: 4 when the v40 interpreter
    /// applies its compatibility hacks, 0 for v35, for monochrome rendering, for tricky fonts and for fonts that ask for native
    /// ClearType behaviour.
    /// </summary>
    public int BackwardCompatibility { get; private set; }

    /// <summary>The offset of the <c>hdmx</c> record of advance widths to use for this size, or -1 (<c>loader->widthp</c>).</summary>
    public int DeviceMetricsOffset { get; private set; } = -1;

    private TtSize(TtFace face, int ppem26Dot6, TtInterpreterVersion version, TtRenderMode mode)
    {
        Face = face;
        Ppem26Dot6 = ppem26Dot6;
        Version = version;
        Mode = mode;
    }

    /// <summary>
    /// Sets up a size and runs the font program and the CVT program for it, as FreeType does on the first hinted glyph load
    /// after a size request.
    /// </summary>
    /// <exception cref="HintingException">The size is invalid, or a program failed: no glyph of the size can be hinted.</exception>
    public static TtSize Create(TtFace face, int ppem26Dot6, TtInterpreterVersion version, TtRenderMode mode, bool pedantic = false)
    {
        var size = new TtSize(face, ppem26Dot6, version, mode);
        size.Reset();
        size.InitBytecode(pedantic);
        return size;
    }

    // FT_Request_Metrics (FT_SIZE_REQUEST_TYPE_NOMINAL, width and height equal) and tt_size_reset.
    private void Reset()
    {
        long scaled = Ppem26Dot6;
        if (scaled <= 0)
            throw new HintingException("The size is not positive.");

        int xScale = FtCalc.DivFix(Ppem26Dot6, Face.UnitsPerEm);
        long ppem = (scaled + 32) >> 6;

        if (ppem > 0xFFFF)
            throw new HintingException("The size is too large.");

        // FreeType refuses a size whose ppem rounds to zero (Invalid_PPem); a glyph load then falls back to no scaling.
        if (ppem == 0)
            throw new HintingException("The size rounds to zero pixels per em.");

        // This bit flag, if set, indicates that the ppems must be rounded to integers. Nearly all TrueType fonts have this
        // bit set, as hinting won't work really well otherwise.
        if ((Face.HeadFlags & 8) != 0)
        {
            // base scaling values on integer ppem values, as mandated by the TrueType specification
            xScale = FtCalc.DivFix((int)ppem << 6, Face.UnitsPerEm);
        }

        Metrics = new TtSizeMetrics
        {
            XScale = xScale,
            YScale = xScale,
            XPpem = (int)ppem,
            YPpem = (int)ppem,
            Scale = xScale,
            Ppem = (int)ppem,
        };

        // For the `MPS' bytecode instruction we need the point size. Resolution 72 dpi, as no resolution is given.
        PointSize = FtCalc.MulDiv(Metrics.Ppem, 64 * 72, 72);
    }

    // tt_size_init_bytecode, tt_size_run_fpgm, tt_loader_init and tt_size_run_prep.
    private void InitBytecode(bool pedantic)
    {
        TtFace face = Face;
        TtExecContext exec = TtExecContext.Rent();

        try
        {
            exec.PedanticHinting = pedantic;
            exec.Version = Version;
            exec.NumGlyphs = face.NumGlyphs;
            exec.HasBlend = face.Normalized is not null;
            exec.BlendCoordinates = face.Normalized is { } normalized ? normalized.Select(v => (int)Math.Round(v * 65536.0)).ToArray() : [];

            exec.MaxFDefs = face.MaxFunctionDefs;
            exec.MaxIDefs = face.MaxInstructionDefs;

            FDefs = new TtDefRecord[exec.MaxFDefs];
            IDefs = new TtDefRecord[exec.MaxIDefs];
            exec.FDefs = FDefs;
            exec.IDefs = IDefs;
            exec.NumFDefs = 0;
            exec.NumIDefs = 0;
            exec.MaxFunc = 0;
            exec.MaxIns = 0;

            // We reserve extra elements on the stack to deal with broken fonts. Some fonts (e.g., `Rubik-Italic.ttf')
            // have buggy hinting bytecode that pushes more values than `maxStackElements' declared in the `maxp' table.
            // For example, `Rubik-Italic.ttf's 'prep' program pushes 255 values but `maxStackElements' is only set to 153.
            //
            // To alleviate this situation we increase the value of `maxStackElements' based on a percentage of
            // `maxStackElements', with a minimum of 128 extra slots. This allows most broken fonts to work without
            // completely disabling hinting, while adding only a small overhead for correctly authored fonts.
            //
            // Use 50% more than declared, with minimum safety margin of 128.
            exec.StackSize = face.MaxStackElements + Math.Max(face.MaxStackElements / 2, 128);
            exec.EnsureStack();

            exec.StoreSize = face.MaxStorage;
            exec.CvtSize = face.Cvt.Length;

            Cvt = new int[exec.CvtSize];
            Storage = new int[exec.StoreSize];

            // reserve twilight zone and set GS before fpgm is executed, just in case, even though fpgm should not touch them
            int nTwilight = face.MaxTwilightPoints + 4; // there are 4 phantom points (do we need this?)
            Twilight.Allocate(nTwilight, 0);

            GraphicsState = TtGraphicsState.Default;

            exec.Twilight = TtGlyphZone.AliasOf(Twilight);
            exec.Cvt = Cvt;
            exec.Storage = Storage;
            exec.PointSize = PointSize;
            exec.Metrics = Metrics;
            exec.BackwardCompatibility = 0;
            exec.ResetWorkingCopies();

            // Fine, now run the font program! In FreeType `fpgm' is run by tt_size_init_bytecode, before tt_loader_init has
            // set the render mode: it always sees the normal mode, whatever the glyphs will be loaded for.
            exec.Mode = TtRenderMode.Normal;
            exec.Grayscale = false;

            exec.ClearCodeRange(TtCodeRange.Cvt);
            exec.ClearCodeRange(TtCodeRange.Glyph);

            if (face.FontProgram.Length > 0)
            {
                // allow font program execution
                exec.SetCodeRange(TtCodeRange.Font, face.FontProgram, face.FontProgram.Length);

                exec.Pts = new TtGlyphZone();

                exec.ResetBudget();
                int error = exec.RunContext(GraphicsState);
                if (error != TtError.Ok)
                    throw new HintingException("The font program failed with error " + error + ".");
            }

            exec.SaveContext(ref GraphicsState);

            // tt_loader_init: the mode and the grayscale flag are set now, and a change from what the context has (the normal
            // mode, no grayscale) requires the CVT program to be run again; it runs at least once anyway.
            exec.BackwardCompatibility = 0;

            bool grayscale = Mode != TtRenderMode.Mono;

            if (Version == TtInterpreterVersion.V40)
                grayscale = false;

            exec.Mode = Mode;
            exec.Grayscale = grayscale;

            RunPrep(exec, face);

            // check whether the cvt program has disabled hinting
            HintingDisabled = (GraphicsState.InstructControl & 1) != 0;

            // check whether GS modifications should be reverted
            if ((GraphicsState.InstructControl & 2) != 0)
                GraphicsState = TtGraphicsState.Default;

            // Toggle backward compatibility according to what font wants, except when
            //
            // 1) we have a `tricky' font that heavily relies on the interpreter to render glyphs correctly, for example
            //    DFKai-SB, or
            // 2) FT_RENDER_MODE_MONO (i.e, monochrome rendering) is requested.
            //
            // In those cases, backward compatibility needs to be turned off to get correct rendering. The rendering is then
            // completely up to the font's programming.
            if (Version == TtInterpreterVersion.V40 && Mode != TtRenderMode.Mono && !face.IsTricky)
                BackwardCompatibility = (GraphicsState.InstructControl & 4) ^ 4;

            // Use the hdmx table if any unless FT_LOAD_COMPUTE_METRICS is set or backward compatibility mode of the v38 or
            // v40 interpreters is active.
            if (!HintingDisabled && BackwardCompatibility == 0 && !face.IsFixedPitch)
                DeviceMetricsOffset = face.GetDeviceMetrics(Metrics.XPpem);

            FDefs = exec.FDefs;
            IDefs = exec.IDefs;
            NumFDefs = exec.NumFDefs;
            NumIDefs = exec.NumIDefs;
            MaxFunc = exec.MaxFunc;
            MaxIns = exec.MaxIns;
        }
        finally
        {
            TtExecContext.Return(exec);
        }
    }

    // tt_size_run_prep
    private void RunPrep(TtExecContext exec, TtFace face)
    {
        // set default GS, twilight points, and storage before CV program can modify them
        GraphicsState = TtGraphicsState.Default;

        // all twilight points are originally zero
        Array.Clear(Twilight.OrgX, 0, Twilight.NPoints);
        Array.Clear(Twilight.OrgY, 0, Twilight.NPoints);
        Array.Clear(Twilight.CurX, 0, Twilight.NPoints);
        Array.Clear(Twilight.CurY, 0, Twilight.NPoints);

        exec.Twilight = TtGlyphZone.AliasOf(Twilight);
        exec.ResetWorkingCopies();

        // clear storage area
        Array.Clear(Storage, 0, Storage.Length);

        // Scale the cvt values to the new ppem. By default, we use the y ppem value for scaling.
        for (int i = 0; i < Cvt.Length; i++)
        {
            // Unscaled CVT values are already stored in 26.6 format. Note that this scaling operation is very sensitive to
            // rounding; the integer division by 64 must be applied to the first argument.
            Cvt[i] = FtCalc.MulFix(face.Cvt[i] / 64, Metrics.Scale);
        }

        exec.ClearCodeRange(TtCodeRange.Glyph);

        if (face.CvtProgram.Length > 0)
        {
            // allow CV program execution
            exec.SetCodeRange(TtCodeRange.Cvt, face.CvtProgram, face.CvtProgram.Length);

            exec.Pts = new TtGlyphZone();

            exec.ResetBudget();
            int error = exec.RunContext(GraphicsState);
            if (error != TtError.Ok)
                throw new HintingException("The CVT program failed with error " + error + ".");
        }

        exec.SaveContext(ref GraphicsState);
    }
}
