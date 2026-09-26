/****************************************************************************
 *
 * ttgload.c
 *
 *   TrueType Glyph Loader (body).
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
 * ftgloadr.c
 *
 *   The FreeType glyph loader (body).
 *
 * Copyright (C) 2002-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttgload.c, ftgloadr.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>A glyph loaded and hinted at one size: an outline in 26.6 pixels and its advance.</summary>
internal sealed class TtHintedGlyph
{
    /// <summary>The point coordinates in 26.6 pixels, y up, with the glyph origin at (0, 0).</summary>
    public int[] X { get; init; } = [];

    public int[] Y { get; init; } = [];

    /// <summary>The point tags: bit 0 is set for on-curve points (the other bits are the hinter's bookkeeping).</summary>
    public byte[] Tags { get; init; } = [];

    /// <summary>The index of the last point of each contour.</summary>
    public int[] ContourEnds { get; init; } = [];

    public int NPoints { get; init; }

    /// <summary>The advance width in 26.6 pixels, rounded to a whole pixel as FreeType reports it for a hinted glyph.</summary>
    public int Advance { get; init; }

    /// <summary>False when the font's CVT program turned hinting off at this size, so the outline is only scaled.</summary>
    public bool IsHinted { get; init; }

    /// <summary>
    /// The last error a glyph program stopped with, or 0 if every program ran to its end. FreeType ignores such an error (the
    /// glyph keeps what the program had done so far) unless asked to be pedantic, and so does the port; this says it happened.
    /// </summary>
    public int ProgramError { get; init; }
}

/// <summary>
/// The TrueType glyph loader (<c>TT_Load_Glyph</c> and what it calls in <c>ttgload.c</c>): reads a glyph, scales it,
/// hints it with the bytecode interpreter, and puts composites together, with the phantom points and the backward
/// compatibility of the v40 interpreter. One instance loads one glyph; it is not shared.
/// </summary>
internal sealed class TtGlyphLoader
{
    // Simple glyph flags.
    private const int OnCurvePoint = 0x01;
    private const int XShortVector = 0x02;
    private const int YShortVector = 0x04;
    private const int RepeatFlag = 0x08;
    private const int XPositive = 0x10; // two meanings depending on X_SHORT_VECTOR
    private const int SameX = 0x10;
    private const int YPositive = 0x20; // two meanings depending on Y_SHORT_VECTOR
    private const int SameY = 0x20;

    // Composite glyph flags.
    private const int ArgsAreWords = 0x0001;
    private const int ArgsAreXyValues = 0x0002;
    private const int RoundXyToGrid = 0x0004;
    private const int WeHaveAScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int WeHaveAnXyScale = 0x0040;
    private const int WeHaveA2x2 = 0x0080;
    private const int WeHaveInstr = 0x0100;
    private const int UseMyMetrics = 0x0200;
    private const int ScaledComponentOffset = 0x0800;

    // FT_OUTLINE_POINTS_MAX and FT_OUTLINE_CONTOURS_MAX
    private const int OutlinePointsMax = 0x7FFF;

    private struct SubGlyph
    {
        public int Flags;
        public int Index;
        public int Arg1;
        public int Arg2;
        public int Xx, Xy, Yx, Yy;
    }

    /// <summary>An outline being put together (<c>FT_Outline</c>): parallel point arrays and contour ends.</summary>
    private sealed class Outline
    {
        public int[] X = new int[64];
        public int[] Y = new int[64];
        public byte[] Tags = new byte[64];
        public ushort[] Contours = new ushort[16];
        public int NPoints;
        public int NContours;

        public void Reset()
        {
            NPoints = 0;
            NContours = 0;
        }

        public void Ensure(int points, int contours)
        {
            if (points > X.Length)
            {
                int size = Math.Max(points, X.Length + (X.Length >> 1));
                Array.Resize(ref X, size);
                Array.Resize(ref Y, size);
                Array.Resize(ref Tags, size);
            }

            if (contours > Contours.Length)
                Array.Resize(ref Contours, Math.Max(contours, Contours.Length + (Contours.Length >> 1)));
        }
    }

    private readonly TtSize _size;
    private readonly TtFace _face;
    private readonly TtExecContext _exec;

    /// <summary>Whether glyphs are hinted (<c>IS_HINTED( load_flags )</c>): false when the CVT program disabled it.</summary>
    private readonly bool _hinted;

    private readonly int _xScale;
    private readonly int _yScale;

    // the glyph being loaded (TT_LoaderRec)
    private int _nContours;
    private int _bboxYMax;
    private int _bboxXMin;
    private int _byteLen;
    private int _leftBearing, _advance, _topBearing, _vAdvance;
    private int _pp1x, _pp1y, _pp2x, _pp2y, _pp3x, _pp3y, _pp4x, _pp4y;
    private int _insPos;
    private int _programError;

    // gloader->base and gloader->current
    private readonly Outline _base = new();
    private readonly Outline _current = new();

    private readonly int[] _compositePath = new int[102];

    /// <summary>
    /// How many glyphs one load may read, components included. FreeType only refuses a glyph that contains itself, so a font of a few
    /// kilobytes can make a composite whose components each name the same composite, and a load takes exponential time; real fonts
    /// use a handful of components.
    /// </summary>
    private const int MaxGlyphsPerLoad = 1024;

    private int _glyphsLeft = MaxGlyphsPerLoad;

    // zone scratch arrays, reused by every hinting call of the loader
    private int[] _orgX = [], _orgY = [], _orusX = [], _orusY = [];
    private readonly TtGlyphZone _zone = new();

    private TtGlyphLoader(TtSize size, TtExecContext exec)
    {
        _size = size;
        _face = size.Face;
        _exec = exec;
        _hinted = !size.HintingDisabled;
        _xScale = size.Metrics.XScale;
        _yScale = size.Metrics.YScale;
        Array.Fill(_compositePath, -1);
    }

    /// <summary>Loads and hints a glyph at the size.</summary>
    /// <exception cref="HintingException">The glyph cannot be loaded: its data is malformed, or a limit is reached.</exception>
    public static TtHintedGlyph Load(TtSize size, int glyphIndex)
    {
        TtExecContext exec = TtExecContext.Rent();

        try
        {
            var loader = new TtGlyphLoader(size, exec);
            loader.PrepareContext();
            return loader.LoadGlyph(glyphIndex);
        }
        finally
        {
            TtExecContext.Return(exec);
        }
    }

    // TT_Load_Context and the part of tt_loader_init that sets the context up for a glyph.
    private void PrepareContext()
    {
        TtExecContext exec = _exec;
        TtFace face = _face;
        TtSize size = _size;

        exec.PedanticHinting = false;
        exec.Version = size.Version;
        exec.Mode = size.Mode;
        exec.Grayscale = size.Version == TtInterpreterVersion.V35 && size.Mode != TtRenderMode.Mono;
        exec.NumGlyphs = face.NumGlyphs;
        exec.HasBlend = face.Normalized is not null;
        exec.BlendCoordinates = face.Normalized is { } normalized ? Array.ConvertAll(normalized, v => (int)Math.Round(v * 65536.0)) : [];

        exec.MaxFDefs = face.MaxFunctionDefs;
        exec.MaxIDefs = face.MaxInstructionDefs;
        exec.FDefs = size.FDefs;
        exec.IDefs = size.IDefs;
        exec.NumFDefs = size.NumFDefs;
        exec.NumIDefs = size.NumIDefs;
        exec.MaxFunc = size.MaxFunc;
        exec.MaxIns = size.MaxIns;

        exec.StackSize = face.MaxStackElements + Math.Max(face.MaxStackElements / 2, 128);
        exec.EnsureStack();
        exec.StoreSize = face.MaxStorage;
        exec.CvtSize = face.Cvt.Length;

        // CVT and storage are not persistent in FreeType: reset them after they might have been modified. Here they are the
        // size's arrays again until a glyph program writes to one, which copies it.
        exec.Cvt = size.Cvt;
        exec.Storage = size.Storage;
        exec.ResetWorkingCopies();

        // The twilight zone starts each glyph as the CVT program left it.
        exec.Twilight = new TtGlyphZone();
        exec.Twilight.CopyFrom(size.Twilight);

        exec.SetProgramRanges(face.FontProgram, face.CvtProgram);

        exec.PointSize = size.PointSize;
        exec.Metrics = size.Metrics;
        exec.BackwardCompatibility = size.BackwardCompatibility;
        exec.IsComposite = false;

        // one budget of instructions and loop work for the glyph and all of its components
        exec.ResetBudget();
    }

    private TtHintedGlyph LoadGlyph(int glyphIndex)
    {
        // main loading loop
        LoadTrueTypeGlyph(glyphIndex, 0);

        // Translate array so that (0,0) is the glyph's origin. Note that this behaviour is independent on the value of bit 1
        // of the `flags' field in the `head' table -- at least major applications like Acroread indicate that.
        int n = _base.NPoints;
        if (_pp1x != 0)
        {
            for (int i = 0; i < n; i++)
                _base.X[i] = unchecked(_base.X[i] - _pp1x);
        }

        // compute_glyph_metrics: the advance, from the hdmx table when the size uses one
        int advance;
        if (_size.DeviceMetricsOffset >= 0)
            advance = _face.Data[_size.DeviceMetricsOffset + glyphIndex] * 64;
        else
            advance = unchecked(_pp2x - _pp1x);

        // ft_glyphslot_grid_fit_metrics
        advance = unchecked(FtCalc.PixRound(advance));

        var x = new int[n];
        var y = new int[n];
        var tags = new byte[n];
        var ends = new int[_base.NContours];
        Array.Copy(_base.X, x, n);
        Array.Copy(_base.Y, y, n);
        Array.Copy(_base.Tags, tags, n);
        for (int i = 0; i < ends.Length; i++)
            ends[i] = _base.Contours[i];

        return new TtHintedGlyph
        {
            X = x,
            Y = y,
            Tags = tags,
            ContourEnds = ends,
            NPoints = n,
            Advance = advance,
            IsHinted = _hinted,
            ProgramError = _programError,
        };
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                             phantom points
    // ---------------------------------------------------------------------------------------------------------------

    /*
     * Calculate the phantom points
     *
     * Defining the right side bearing (rsb) as
     *
     *   rsb = aw - (lsb + xmax - xmin)
     *
     * (with `aw' the advance width, `lsb' the left side bearing, and `xmin' and `xmax' the glyph's minimum and maximum x
     * value), the OpenType specification defines the initial position of horizontal phantom points as
     *
     *   pp1 = (round(xmin - lsb), 0)      ,
     *   pp2 = (round(pp1 + aw), 0)        .
     *
     * Note that the rounding to the grid (in the device space) is not documented currently in the specification.
     *
     * However, the specification lacks the precise definition of vertical phantom points. Greg Hitchcock provided the
     * following explanation.
     *
     * - a `vmtx' table is present
     *
     *   For any glyph, the minimum and maximum y values (`ymin' and `ymax') are given in the `glyf' table, the top side
     *   bearing (tsb) and advance height (ah) are given in the `vmtx' table. The bottom side bearing (bsb) is then
     *   calculated as
     *
     *     bsb = ah - (tsb + ymax - ymin)       ,
     *
     *   and the initial position of vertical phantom points is
     *
     *     pp3 = (x, round(ymax + tsb))       ,
     *     pp4 = (x, round(pp3 - ah))         .
     *
     *   See below for value `x'.
     *
     * - no `vmtx' table in the font
     *
     *   If there is an `OS/2' table, we set
     *
     *     DefaultAscender = sTypoAscender       ,
     *     DefaultDescender = sTypoDescender     ,
     *
     *   otherwise we use data from the `hhea' table:
     *
     *     DefaultAscender = Ascender         ,
     *     DefaultDescender = Descender       .
     *
     *   With these two variables we can now set
     *
     *     ah = DefaultAscender - sDefaultDescender    ,
     *     tsb = DefaultAscender - yMax                ,
     *
     *   and proceed as if a `vmtx' table was present.
     *
     * Usually we have
     *
     *   x = aw / 2      ,                                                (1)
     *
     * but there is one compatibility case where it can be set to
     *
     *   x = -DefaultDescender - ((DefaultAscender - DefaultDescender - aw) / 2)     .      (2)
     *
     * and another one with
     *
     *   x = 0     .                                                      (3)
     *
     * In Windows, the history of those values is quite complicated, depending on the hinting engine (that is, the graphics
     * framework).
     *
     *   framework        from                 to       formula
     *  ----------------------------------------------------------
     *    GDI       Windows 98               current      (1)
     *              (Windows 2000 for NT)
     *    GDI+      Windows XP               Windows 7    (2)
     *    GDI+      Windows 8                current      (3)
     *    DWrite    Windows 7                current      (3)
     *
     * For simplicity, FreeType uses (1) for grayscale subpixel hinting and (3) for everything else.
     */
    private void SetPhantomPoints()
    {
        _pp1x = _bboxXMin - _leftBearing;
        _pp1y = 0;
        _pp2x = _pp1x + _advance;
        _pp2y = 0;

        _pp3x = 0;
        _pp3y = _bboxYMax + _topBearing;
        _pp4x = 0;
        _pp4y = _pp3y - _vAdvance;

        if (_size.Version == TtInterpreterVersion.V40 && _size.Mode != TtRenderMode.Mono)
        {
            _pp3x = _advance / 2;
            _pp4x = _advance / 2;
        }
    }

    private void ScalePhantomPoints()
    {
        _pp1x = FtCalc.MulFix(_pp1x, _xScale);
        _pp2x = FtCalc.MulFix(_pp2x, _xScale);

        // pp1.y and pp2.y are always zero
        _pp3x = FtCalc.MulFix(_pp3x, _xScale);
        _pp3y = FtCalc.MulFix(_pp3y, _yScale);
        _pp4x = FtCalc.MulFix(_pp4x, _xScale);
        _pp4y = FtCalc.MulFix(_pp4y, _yScale);
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                               load_truetype_glyph
    // ---------------------------------------------------------------------------------------------------------------

    private ReadOnlySpan<byte> GlyphData(int offset, int length)
    {
        long start = (long)_face.GlyfOffset + offset;
        if (start < 0 || start + length > _face.Data.Length)
            throw new HintingException("A glyph's data reaches past the end of the font.");

        return _face.Data.AsSpan((int)start, length);
    }

    private void LoadTrueTypeGlyph(int glyphIndex, int recurseCount)
    {
        // arbitrary recursion limit
        if (recurseCount > 100)
            throw new HintingException("Composite glyphs are nested too deeply.");

        if (--_glyphsLeft < 0)
            throw new HintingException("A glyph is made of too many components.");

        // check glyph index
        if ((uint)glyphIndex >= (uint)_face.NumGlyphs)
            throw new HintingException("The glyph index is invalid.");

        // Set `offset' to the start of the glyph relative to the start of the `glyf' table, and `byte_len' to the length of
        // the glyph in bytes.
        int offset = _face.GetLocation(glyphIndex, out int len);
        _byteLen = len;

        ReadOnlySpan<byte> glyph = default;

        if (_byteLen > 0)
        {
            if (_face.GlyfOffset == 0)
                throw new HintingException("There is no glyf table but a non-zero loca entry.");

            glyph = GlyphData(offset, _byteLen);

            // read glyph header first
            if (glyph.Length < 10)
                throw new HintingException("A glyph header is truncated.");

            // the glyph's bounding box: xMin, yMin, xMax, yMax; only xMin and yMax are used
            _nContours = BinaryPrimitives.ReadInt16BigEndian(glyph);
            _bboxXMin = BinaryPrimitives.ReadInt16BigEndian(glyph[2..]);
            _bboxYMax = BinaryPrimitives.ReadInt16BigEndian(glyph[8..]);
        }

        // a space glyph
        if (_byteLen == 0 || _nContours == 0)
        {
            _bboxXMin = 0;
            _bboxYMax = 0;
        }

        // the metrics must be computed after loading the glyph header since we need the glyph's `yMax' value in case the
        // vertical metrics must be emulated
        _face.GetHMetrics(glyphIndex, out _leftBearing, out _advance);
        _face.GetVMetrics(glyphIndex, _bboxYMax, out _topBearing, out _vAdvance);

        SetPhantomPoints();

        // shortcut for empty glyphs
        if (_byteLen == 0 || _nContours == 0)
        {
            ApplyPhantomDeltas(glyphIndex);

            // scale phantom points; they get rounded in `TT_Hint_Glyph' (which an empty glyph never reaches)
            ScalePhantomPoints();
            return;
        }

        ReadOnlySpan<byte> frame = glyph[10..];

        // if it is a simple glyph, load it
        if (_nContours > 0)
        {
            LoadSimpleGlyph(frame, glyphIndex);
            ProcessSimpleGlyph(glyphIndex);
            AddCurrent();
        }

        // otherwise, load a composite!
        else
        {
            // normalize the `n_contours' value
            _nContours = -1;

            // clear the nodes filled by sibling chains
            for (int i = recurseCount; i < _compositePath.Length; i++)
                _compositePath[i] = -1;

            // check whether we already have a composite glyph with this index
            if (Array.IndexOf(_compositePath, glyphIndex) >= 0)
                throw new HintingException("A composite glyph contains itself.");

            _compositePath[recurseCount] = glyphIndex;

            int startPoint = _base.NPoints;
            int startContour = _base.NContours;

            // for each subglyph, read composite header
            SubGlyph[] subglyphs = ReadCompositeGlyph(frame, offset, out int insPos);

            // store the offset of instructions
            _insPos = insPos;

            ApplyCompositeDeltas(glyphIndex, subglyphs);

            // scale phantom points; they get rounded in `TT_Hint_Glyph'
            ScalePhantomPoints();

            int numPoints = startPoint;
            int oldByteLen = _byteLen;
            int savedInsPos = insPos;

            // read each subglyph independently
            for (int n = 0; n < subglyphs.Length; n++)
            {
                SubGlyph subglyph = subglyphs[n];

                int pp1x = _pp1x, pp1y = _pp1y, pp2x = _pp2x, pp2y = _pp2y, pp3x = _pp3x, pp3y = _pp3y, pp4x = _pp4x, pp4y = _pp4y;

                int numBasePoints = _base.NPoints;

                LoadTrueTypeGlyph(subglyph.Index, recurseCount + 1);

                // restore phantom points if necessary
                if ((subglyph.Flags & UseMyMetrics) == 0)
                {
                    _pp1x = pp1x; _pp1y = pp1y; _pp2x = pp2x; _pp2y = pp2y;
                    _pp3x = pp3x; _pp3y = pp3y; _pp4x = pp4x; _pp4y = pp4y;
                }

                numPoints = _base.NPoints;

                if (numPoints == numBasePoints)
                    continue;

                // _base.outline consists of three parts:
                //
                // 0 ----> start_point ----> num_base_points ----> n_points
                //    (1)               (2)                   (3)
                //
                // (1) points that exist from the beginning
                // (2) component points that have been loaded so far
                // (3) points of the newly loaded component
                ProcessCompositeComponent(subglyph, startPoint, numBasePoints);
            }

            _byteLen = oldByteLen;

            // process the glyph
            _insPos = savedInsPos;
            if (_hinted && subglyphs.Length > 0 && (subglyphs[^1].Flags & WeHaveInstr) != 0 && numPoints > startPoint)
                ProcessCompositeGlyph(startPoint, startContour);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                               simple glyphs
    // ---------------------------------------------------------------------------------------------------------------

    // TT_Load_Simple_Glyph: reads the glyph into `current'; the four phantom points are put after its points.
    private void LoadSimpleGlyph(ReadOnlySpan<byte> frame, int glyphIndex)
    {
        int nContours = _nContours;
        int p = 0;

        _current.Reset();

        // check that we can add the contours to the glyph
        if (_base.NContours + nContours > OutlinePointsMax)
            throw new HintingException("A glyph has too many contours.");

        // check space for contours array + instructions count
        if (nContours >= 0xFFF || p + 2 * nContours + 2 > frame.Length)
            throw new HintingException("A glyph's contour array is truncated.");

        _current.Ensure(0, nContours);

        // reading the contours' endpoints & number of points
        int last = -1;
        for (int c = 0; c < nContours; c++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(frame[p..]);
            p += 2;

            if (end <= last)
                throw new HintingException("A glyph's contours are not in increasing order.");

            _current.Contours[c] = (ushort)end;
            last = end;
        }

        int nPoints = last + 1;

        // note that we will add four phantom points later
        if (_base.NPoints + nPoints + 4 > OutlinePointsMax)
            throw new HintingException("A glyph has too many points.");

        _current.Ensure(nPoints + 4, nContours);

        int nIns = BinaryPrimitives.ReadUInt16BigEndian(frame[p..]);
        p += 2;

        // check instructions size
        if (p + nIns > frame.Length)
            throw new HintingException("A glyph has more instructions than data.");

        if (_hinted)
        {
            // we don't trust `maxSizeOfInstructions' in the `maxp' table and thus allocate the bytecode array size by ourselves
            _exec.GlyphIns = nIns > 0 ? frame.Slice(p, nIns).ToArray() : [];
            _exec.GlyphSize = nIns;
        }

        p += nIns;

        // reading the point tags
        byte[] tags = _current.Tags;
        int flag = 0;

        while (flag < nPoints)
        {
            if (p + 1 > frame.Length)
                throw new HintingException("A glyph's flags are truncated.");

            byte c = frame[p++];
            tags[flag++] = c;

            if ((c & RepeatFlag) != 0)
            {
                if (p + 1 > frame.Length)
                    throw new HintingException("A glyph's flags are truncated.");

                int count = frame[p++];
                if (flag + count > nPoints)
                    throw new HintingException("A glyph's flags repeat past its points.");

                for (; count > 0; count--)
                    tags[flag++] = c;
            }
        }

        // reading the X coordinates
        int x = 0;
        for (int i = 0; i < nPoints; i++)
        {
            int delta = 0;
            int f = tags[i];

            if ((f & XShortVector) != 0)
            {
                if (p + 1 > frame.Length)
                    throw new HintingException("A glyph's x coordinates are truncated.");

                delta = frame[p++];
                if ((f & XPositive) == 0)
                    delta = -delta;
            }
            else if ((f & SameX) == 0)
            {
                if (p + 2 > frame.Length)
                    throw new HintingException("A glyph's x coordinates are truncated.");

                delta = BinaryPrimitives.ReadInt16BigEndian(frame[p..]);
                p += 2;
            }

            x += delta;
            _current.X[i] = x;
        }

        // reading the Y coordinates
        int y = 0;
        for (int i = 0; i < nPoints; i++)
        {
            int delta = 0;
            int f = tags[i];

            if ((f & YShortVector) != 0)
            {
                if (p + 1 > frame.Length)
                    throw new HintingException("A glyph's y coordinates are truncated.");

                delta = frame[p++];
                if ((f & YPositive) == 0)
                    delta = -delta;
            }
            else if ((f & SameY) == 0)
            {
                if (p + 2 > frame.Length)
                    throw new HintingException("A glyph's y coordinates are truncated.");

                delta = BinaryPrimitives.ReadInt16BigEndian(frame[p..]);
                p += 2;
            }

            y += delta;
            _current.Y[i] = y;

            // the cast is for stupid compilers
            tags[i] = (byte)(f & OnCurvePoint);
        }

        _current.NPoints = nPoints;
        _current.NContours = nContours;
    }

    // TT_Process_Simple_Glyph: once a simple glyph has been loaded, it needs to be processed. Usually, this means scaling and
    // hinting through bytecode interpretation.
    private void ProcessSimpleGlyph(int glyphIndex)
    {
        int nPoints = _current.NPoints;
        int[] xs = _current.X;
        int[] ys = _current.Y;

        // set phantom points
        xs[nPoints] = _pp1x; ys[nPoints] = _pp1y;
        xs[nPoints + 1] = _pp2x; ys[nPoints + 1] = _pp2y;
        xs[nPoints + 2] = _pp3x; ys[nPoints + 2] = _pp3y;
        xs[nPoints + 3] = _pp4x; ys[nPoints + 3] = _pp4y;
        _current.Tags[nPoints] = 0;
        _current.Tags[nPoints + 1] = 0;
        _current.Tags[nPoints + 2] = 0;
        _current.Tags[nPoints + 3] = 0;

        nPoints += 4;

        // Deltas apply to the unscaled data.
        double[]? scaledX = null, scaledY = null;
        if (_face.Gvar is not null)
            ApplySimpleDeltas(glyphIndex, nPoints, xs, ys, out scaledX, out scaledY);

        if (_hinted)
        {
            EnsureScratch(nPoints);
            Array.Copy(xs, _orusX, nPoints);
            Array.Copy(ys, _orusY, nPoints);
        }

        // scale the glyph
        if (scaledX is not null && scaledY is not null)
        {
            // an instance: the deltas were added to the unscaled points and the result is rounded once to 26.6
            for (int i = 0; i < nPoints; i++)
            {
                xs[i] = (int)Math.Floor(scaledX[i] * (_xScale / 65536.0) + 0.5);
                ys[i] = (int)Math.Floor(scaledY[i] * (_yScale / 65536.0) + 0.5);
            }
        }
        else
        {
            for (int i = 0; i < nPoints; i++)
            {
                xs[i] = FtCalc.MulFix(xs[i], _xScale);
                ys[i] = FtCalc.MulFix(ys[i], _yScale);
            }
        }

        _pp1x = xs[nPoints - 4]; _pp1y = ys[nPoints - 4];
        _pp2x = xs[nPoints - 3]; _pp2y = ys[nPoints - 3];
        _pp3x = xs[nPoints - 2]; _pp3y = ys[nPoints - 2];
        _pp4x = xs[nPoints - 1]; _pp4y = ys[nPoints - 1];

        if (_hinted)
        {
            // tt_prepare_zone( &loader->zone, &gloader->current, 0, 0 )
            _zone.NPoints = nPoints;
            _zone.NContours = _current.NContours;
            _zone.OrgX = _orgX;
            _zone.OrgY = _orgY;
            _zone.CurX = xs;
            _zone.CurY = ys;
            _zone.OrusX = _orusX;
            _zone.OrusY = _orusY;
            _zone.Tags = _current.Tags;
            _zone.Contours = _current.Contours;
            _zone.FirstPoint = 0;

            HintGlyph(_zone, false);
        }
    }

    private void EnsureScratch(int points)
    {
        if (_orgX.Length < points)
        {
            int size = Math.Max(points, 64);
            _orgX = new int[size];
            _orgY = new int[size];
            _orusX = new int[size];
            _orusY = new int[size];
        }
    }

    // gloader->current is added to gloader->base (FT_GlyphLoader_Add).
    private void AddCurrent()
    {
        int nPoints = _current.NPoints;
        int nContours = _current.NContours;

        _base.Ensure(_base.NPoints + nPoints + 4, _base.NContours + nContours);

        // adjust contours count in newest outline
        for (int n = 0; n < nContours; n++)
            _base.Contours[_base.NContours + n] = (ushort)(_current.Contours[n] + _base.NPoints);

        Array.Copy(_current.X, 0, _base.X, _base.NPoints, nPoints);
        Array.Copy(_current.Y, 0, _base.Y, _base.NPoints, nPoints);
        Array.Copy(_current.Tags, 0, _base.Tags, _base.NPoints, nPoints);

        _base.NPoints += nPoints;
        _base.NContours += nContours;

        _current.Reset();
    }

    // TT_Hint_Glyph: hint the glyph using the zone prepared by the caller. Note that the zone is supposed to include four
    // phantom points.
    private void HintGlyph(TtGlyphZone zone, bool isComposite)
    {
        TtExecContext exec = _exec;
        int nIns = exec.GlyphSize;
        int n = zone.NPoints;

        // save original point positions in `org' array
        if (nIns > 0)
        {
            Array.Copy(zone.CurX, zone.OrgX, n);
            Array.Copy(zone.CurY, zone.OrgY, n);
        }

        // XXX: UNDOCUMENTED! Hinting instructions of a composite glyph completely refer to the (already) hinted subglyphs.
        if (isComposite)
        {
            exec.Metrics.XScale = 1 << 16;
            exec.Metrics.YScale = 1 << 16;

            Array.Copy(zone.CurX, zone.OrusX, n);
            Array.Copy(zone.CurY, zone.OrusY, n);
        }
        else
        {
            exec.Metrics.XScale = _xScale;
            exec.Metrics.YScale = _yScale;
        }

        // round phantom points
        zone.CurX[n - 4] = FtCalc.PixRound(zone.CurX[n - 4]);
        zone.CurX[n - 3] = FtCalc.PixRound(zone.CurX[n - 3]);
        zone.CurY[n - 2] = FtCalc.PixRound(zone.CurY[n - 2]);
        zone.CurY[n - 1] = FtCalc.PixRound(zone.CurY[n - 1]);

        if (nIns > 0)
        {
            exec.SetCodeRange(TtCodeRange.Glyph, exec.GlyphIns, nIns);

            exec.IsComposite = isComposite;
            exec.Pts = zone;

            // An error is ignored (FreeType does the same unless it is asked to be pedantic): the glyph keeps what the
            // program had done to it up to that point.
            int error = exec.RunContext(_size.GraphicsState);
            if (error != TtError.Ok)
                _programError = error;
        }

        // Save possibly modified glyph phantom points unless in v40 backward compatibility mode, where no movement on the x
        // axis means no reason to change bearings or advance widths.
        if (exec.BackwardCompatibility != 0)
            return;

        _pp1x = zone.CurX[n - 4]; _pp1y = zone.CurY[n - 4];
        _pp2x = zone.CurX[n - 3]; _pp2y = zone.CurY[n - 3];
        _pp3x = zone.CurX[n - 2]; _pp3y = zone.CurY[n - 2];
        _pp4x = zone.CurX[n - 1]; _pp4y = zone.CurY[n - 1];
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                              composite glyphs
    // ---------------------------------------------------------------------------------------------------------------

    // TT_Load_Composite_Glyph
    private SubGlyph[] ReadCompositeGlyph(ReadOnlySpan<byte> frame, int glyphOffset, out int insPos)
    {
        int p = 0;
        var list = new System.Collections.Generic.List<SubGlyph>(4);
        SubGlyph subglyph;

        do
        {
            // check space
            if (p + 4 > frame.Length)
                throw new HintingException("A composite glyph is truncated.");

            subglyph = new SubGlyph
            {
                Flags = BinaryPrimitives.ReadUInt16BigEndian(frame[p..]),
                Index = BinaryPrimitives.ReadUInt16BigEndian(frame[(p + 2)..]),
            };
            p += 4;

            // we reject composites that have components with invalid glyph indices
            if (subglyph.Index >= _face.NumGlyphs)
                throw new HintingException("A composite glyph has a component with an invalid glyph index.");

            // check space
            int count = 2;
            if ((subglyph.Flags & ArgsAreWords) != 0)
                count += 2;
            if ((subglyph.Flags & WeHaveAScale) != 0)
                count += 2;
            else if ((subglyph.Flags & WeHaveAnXyScale) != 0)
                count += 4;
            else if ((subglyph.Flags & WeHaveA2x2) != 0)
                count += 8;

            if (p + count > frame.Length)
                throw new HintingException("A composite glyph is truncated.");

            // read arguments
            if ((subglyph.Flags & ArgsAreXyValues) != 0)
            {
                if ((subglyph.Flags & ArgsAreWords) != 0)
                {
                    subglyph.Arg1 = BinaryPrimitives.ReadInt16BigEndian(frame[p..]);
                    subglyph.Arg2 = BinaryPrimitives.ReadInt16BigEndian(frame[(p + 2)..]);
                    p += 4;
                }
                else
                {
                    subglyph.Arg1 = (sbyte)frame[p];
                    subglyph.Arg2 = (sbyte)frame[p + 1];
                    p += 2;
                }
            }
            else
            {
                if ((subglyph.Flags & ArgsAreWords) != 0)
                {
                    subglyph.Arg1 = BinaryPrimitives.ReadUInt16BigEndian(frame[p..]);
                    subglyph.Arg2 = BinaryPrimitives.ReadUInt16BigEndian(frame[(p + 2)..]);
                    p += 4;
                }
                else
                {
                    subglyph.Arg1 = frame[p];
                    subglyph.Arg2 = frame[p + 1];
                    p += 2;
                }
            }

            // read transform
            int xx = 0x10000, yy = 0x10000, xy = 0, yx = 0;

            if ((subglyph.Flags & WeHaveAScale) != 0)
            {
                xx = BinaryPrimitives.ReadInt16BigEndian(frame[p..]) * 4;
                p += 2;
                yy = xx;
            }
            else if ((subglyph.Flags & WeHaveAnXyScale) != 0)
            {
                xx = BinaryPrimitives.ReadInt16BigEndian(frame[p..]) * 4;
                yy = BinaryPrimitives.ReadInt16BigEndian(frame[(p + 2)..]) * 4;
                p += 4;
            }
            else if ((subglyph.Flags & WeHaveA2x2) != 0)
            {
                xx = BinaryPrimitives.ReadInt16BigEndian(frame[p..]) * 4;
                yx = BinaryPrimitives.ReadInt16BigEndian(frame[(p + 2)..]) * 4;
                xy = BinaryPrimitives.ReadInt16BigEndian(frame[(p + 4)..]) * 4;
                yy = BinaryPrimitives.ReadInt16BigEndian(frame[(p + 6)..]) * 4;
                p += 8;
            }

            subglyph.Xx = xx;
            subglyph.Xy = xy;
            subglyph.Yx = yx;
            subglyph.Yy = yy;

            list.Add(subglyph);

            if (list.Count > 0xFFFF)
                throw new HintingException("A composite glyph has too many components.");
        }
        while ((subglyph.Flags & MoreComponents) != 0);

        // we must undo the FT_FRAME_ENTER in order to point to the composite instructions, if we find some. We will process
        // them later.
        insPos = (int)((long)_face.GlyfOffset + glyphOffset + 10 + p);

        return list.ToArray();
    }

    // TT_Process_Composite_Component: once a composite component has been loaded, it needs to be processed. Usually, this
    // means transforming and translating.
    private void ProcessCompositeComponent(SubGlyph subglyph, int startPoint, int numBasePoints)
    {
        int end = _base.NPoints;

        bool haveScale = (subglyph.Flags & (WeHaveAScale | WeHaveAnXyScale | WeHaveA2x2)) != 0;

        // perform the transform required for this subglyph
        if (haveScale)
        {
            for (int i = numBasePoints; i < end; i++)
            {
                int vx = _base.X[i];
                int vy = _base.Y[i];

                _base.X[i] = unchecked(FtCalc.MulFix(vx, subglyph.Xx) + FtCalc.MulFix(vy, subglyph.Xy));
                _base.Y[i] = unchecked(FtCalc.MulFix(vx, subglyph.Yx) + FtCalc.MulFix(vy, subglyph.Yy));
            }
        }

        int x, y;

        // get offset
        if ((subglyph.Flags & ArgsAreXyValues) == 0)
        {
            int numPoints = _base.NPoints;
            uint k = (uint)subglyph.Arg1;
            uint l = (uint)subglyph.Arg2;

            // match l-th point of the newly loaded component to the k-th point of the previously loaded components.

            // change to the point numbers used by our outline
            k += (uint)startPoint;
            l += (uint)numBasePoints;
            if (k >= (uint)numBasePoints || l >= (uint)numPoints)
                throw new HintingException("A composite glyph matches points that do not exist.");

            x = unchecked(_base.X[k] - _base.X[l]);
            y = unchecked(_base.Y[k] - _base.Y[l]);
        }
        else
        {
            x = subglyph.Arg1;
            y = subglyph.Arg2;

            if (x == 0 && y == 0)
                return;

            // Use a default value dependent on TT_CONFIG_OPTION_COMPONENT_OFFSET_SCALED (not defined). This is useful for
            // old TT fonts which don't set the xxx_COMPONENT_OFFSET bit.
            if (haveScale && (subglyph.Flags & ScaledComponentOffset) != 0)
            {
                // This algorithm is a guess and works much better than the one Apple documents.
                int macXScale = FtTrigon.Hypot(subglyph.Xx, subglyph.Xy);
                int macYScale = FtTrigon.Hypot(subglyph.Yy, subglyph.Yx);

                x = FtCalc.MulFix(x, macXScale);
                y = FtCalc.MulFix(y, macYScale);
            }

            x = FtCalc.MulFix(x, _xScale);
            y = FtCalc.MulFix(y, _yScale);

            if ((subglyph.Flags & RoundXyToGrid) != 0 && _hinted)
            {
                if (_exec.BackwardCompatibility == 0)
                    x = FtCalc.PixRound(x);

                y = FtCalc.PixRound(y);
            }
        }

        if (x != 0 || y != 0)
        {
            for (int i = numBasePoints; i < end; i++)
            {
                _base.X[i] = unchecked(_base.X[i] + x);
                _base.Y[i] = unchecked(_base.Y[i] + y);
            }
        }
    }

    // TT_Process_Composite_Glyph: this is slightly different from TT_Process_Simple_Glyph, in that its sole purpose is to hint
    // the glyph.
    private void ProcessCompositeGlyph(int startPoint, int startContour)
    {
        TtExecContext exec = _exec;
        int n = _base.NPoints;

        // make room for phantom points
        _base.Ensure(n + 4, _base.NContours);

        _base.X[n] = _pp1x; _base.Y[n] = _pp1y;
        _base.X[n + 1] = _pp2x; _base.Y[n + 1] = _pp2y;
        _base.X[n + 2] = _pp3x; _base.Y[n + 2] = _pp3y;
        _base.X[n + 3] = _pp4x; _base.Y[n + 3] = _pp4y;
        _base.Tags[n] = 0;
        _base.Tags[n + 1] = 0;
        _base.Tags[n + 2] = 0;
        _base.Tags[n + 3] = 0;

        exec.GlyphIns = [];
        exec.GlyphSize = 0;

        // TT_Load_Composite_Glyph only gives us the offset of instructions so we read them here
        if (_insPos < 0 || (long)_insPos + 2 > _face.Data.Length)
            throw new HintingException("A composite glyph's instructions are outside the font.");

        int nIns = BinaryPrimitives.ReadUInt16BigEndian(_face.Data.AsSpan(_insPos));

        if (nIns == 0)
            return;

        // don't trust `maxSizeOfInstructions'; only do a rough safety check
        if (nIns > _byteLen)
            throw new HintingException("A composite glyph has more instructions than data.");

        if ((long)_insPos + 2 + nIns > _face.Data.Length)
            throw new HintingException("A composite glyph's instructions are outside the font.");

        exec.GlyphIns = _face.Data.AsSpan(_insPos + 2, nIns).ToArray();
        exec.GlyphSize = nIns;

        // tt_prepare_zone( &loader->zone, &loader->gloader->base, start_point, start_contour ): the zone is a copy of the
        // part of the outline from start_point, with the phantom points
        int zonePoints = n + 4 - startPoint;
        int zoneContours = _base.NContours - startContour;

        EnsureScratch(zonePoints);
        var curX = new int[zonePoints];
        var curY = new int[zonePoints];
        var tags = new byte[zonePoints];
        var contours = new ushort[zoneContours];

        Array.Copy(_base.X, startPoint, curX, 0, zonePoints);
        Array.Copy(_base.Y, startPoint, curY, 0, zonePoints);
        Array.Copy(_base.Tags, startPoint, tags, 0, zonePoints);
        Array.Copy(_base.Contours, startContour, contours, 0, zoneContours);

        _zone.NPoints = zonePoints;
        _zone.NContours = zoneContours;
        _zone.OrgX = _orgX;
        _zone.OrgY = _orgY;
        _zone.CurX = curX;
        _zone.CurY = curY;
        _zone.OrusX = _orusX;
        _zone.OrusY = _orusY;
        _zone.Tags = tags;
        _zone.Contours = contours;
        _zone.FirstPoint = startPoint;

        // Some points are likely touched during execution of instructions on components. So let's untouch them.
        for (int i = 0; i < zonePoints - 4; i++)
            tags[i] = (byte)(tags[i] & ~FtTag.TouchBoth);

        HintGlyph(_zone, true);

        Array.Copy(curX, 0, _base.X, startPoint, zonePoints);
        Array.Copy(curY, 0, _base.Y, startPoint, zonePoints);
        Array.Copy(tags, 0, _base.Tags, startPoint, zonePoints);
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                     variation instances (not bit-exact, see PORTING-NOTES.md)
    // ---------------------------------------------------------------------------------------------------------------

    // The phantom point deltas of a glyph with no outline of its own.
    private void ApplyPhantomDeltas(int glyphIndex)
    {
        if (_face.Gvar is not { } gvar || _face.Normalized is not { } normalized)
            return;

        var dx = new double[4];
        var dy = new double[4];
        if (!gvar.TryAddDeltas(glyphIndex, normalized, 4, null, null, null, dx, dy))
            return;

        ApplyPhantomDeltasTo(dx, dy, 0);
    }

    private void ApplyPhantomDeltasTo(double[] dx, double[] dy, int first)
    {
        // With an HVAR table the advance is already the instance's, and the horizontal phantom points follow it.
        if (_face.InstanceAdvance is null)
        {
            _pp1x += (int)Math.Round(dx[first]);
            _pp1y += (int)Math.Round(dy[first]);
            _pp2x += (int)Math.Round(dx[first + 1]);
            _pp2y += (int)Math.Round(dy[first + 1]);
        }

        _pp3x += (int)Math.Round(dx[first + 2]);
        _pp3y += (int)Math.Round(dy[first + 2]);
        _pp4x += (int)Math.Round(dx[first + 3]);
        _pp4y += (int)Math.Round(dy[first + 3]);
    }

    private void ApplySimpleDeltas(int glyphIndex, int total, int[] xs, int[] ys, out double[]? scaledX, out double[]? scaledY)
    {
        scaledX = null;
        scaledY = null;

        if (_face.Gvar is not { } gvar || _face.Normalized is not { } normalized)
            return;

        int points = total - 4;
        var originalX = new double[total];
        var originalY = new double[total];
        for (int i = 0; i < points; i++)
        {
            originalX[i] = xs[i];
            originalY[i] = ys[i];
        }

        var ends = new int[_current.NContours];
        for (int i = 0; i < ends.Length; i++)
            ends[i] = _current.Contours[i];

        var dx = new double[total];
        var dy = new double[total];
        if (!gvar.TryAddDeltas(glyphIndex, normalized, total, originalX, originalY, ends, dx, dy))
            return;

        scaledX = new double[total];
        scaledY = new double[total];
        for (int i = 0; i < total; i++)
        {
            double vx = (i < points ? xs[i] : PhantomX(i - points)) + dx[i];
            double vy = (i < points ? ys[i] : PhantomY(i - points)) + dy[i];

            scaledX[i] = vx;
            scaledY[i] = vy;

            // the unscaled outline of an instance is rounded to whole font units
            if (i < points)
            {
                xs[i] = (int)Math.Round(vx);
                ys[i] = (int)Math.Round(vy);
            }
        }

        // the phantom points, unscaled and rounded like the points, for the zone's unscaled copy
        xs[points] = (int)Math.Round(scaledX[points]);
        ys[points] = (int)Math.Round(scaledY[points]);
        xs[points + 1] = (int)Math.Round(scaledX[points + 1]);
        ys[points + 1] = (int)Math.Round(scaledY[points + 1]);
        xs[points + 2] = (int)Math.Round(scaledX[points + 2]);
        ys[points + 2] = (int)Math.Round(scaledY[points + 2]);
        xs[points + 3] = (int)Math.Round(scaledX[points + 3]);
        ys[points + 3] = (int)Math.Round(scaledY[points + 3]);

        if (_face.InstanceAdvance is not null)
        {
            // the horizontal phantom points keep the instance's advance from HVAR
            scaledX[points] = _pp1x;
            scaledX[points + 1] = _pp2x;
            xs[points] = _pp1x;
            xs[points + 1] = _pp2x;
        }
    }

    private double PhantomX(int i) => i switch { 0 => _pp1x, 1 => _pp2x, 2 => _pp3x, _ => _pp4x };

    private double PhantomY(int i) => i switch { 0 => _pp1y, 1 => _pp2y, 2 => _pp3y, _ => _pp4y };

    private void ApplyCompositeDeltas(int glyphIndex, SubGlyph[] subglyphs)
    {
        if (_face.Gvar is not { } gvar || _face.Normalized is not { } normalized)
            return;

        // applying deltas for anchor points doesn't make sense, but we don't have to specially check this since unused delta
        // values are zero anyways
        int total = subglyphs.Length + 4;
        var dx = new double[total];
        var dy = new double[total];
        if (!gvar.TryAddDeltas(glyphIndex, normalized, total, null, null, null, dx, dy))
            return;

        for (int i = 0; i < subglyphs.Length; i++)
        {
            if ((subglyphs[i].Flags & ArgsAreXyValues) != 0)
            {
                subglyphs[i].Arg1 = (short)Math.Round(subglyphs[i].Arg1 + dx[i]);
                subglyphs[i].Arg2 = (short)Math.Round(subglyphs[i].Arg2 + dy[i]);
            }
        }

        ApplyPhantomDeltasTo(dx, dy, subglyphs.Length);
    }
}
