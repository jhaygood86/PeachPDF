using Microsoft.Graphics.Canvas.Text;
using PeachDrawing.Core;
using PeachDrawing.Text;
using System;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.Win2D;

/// <summary>
/// What a <see cref="Win2dRenderContext"/> in DirectWrite mode uses to turn a family name into a font: the installed families DirectWrite
/// knows, and the fonts a document added, which are written to a folder the context owns (Win2D loads a font from a file address, not from
/// memory) and deleted again with it.
/// </summary>
internal sealed class DirectWriteFonts : IDisposable
{
    private readonly Win2dRenderContext _context;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<AddedFace>> _added = new(StringComparer.OrdinalIgnoreCase);
    private string? _folder;
    private bool _disposed;

    private readonly record struct AddedFace(string FileAddress, string FamilyName, FontFaceDescriptors Descriptors);

    public DirectWriteFonts(Win2dRenderContext context) => _context = context;

    /// <summary>The families DirectWrite lists as installed.</summary>
    public static IReadOnlyList<string> InstalledFamilyNames { get; } = CanvasTextFormat.GetSystemFontFamilies();

    /// <summary>Writes <paramref name="data"/> to the context's font folder so that <paramref name="family"/> can be drawn with it.</summary>
    public void AddFont(byte[] data, string family, FontFaceDescriptors descriptors)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _folder ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PeachDrawing.Win2D", Guid.NewGuid().ToString("N"))).FullName;

            var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + Extension(data));
            File.WriteAllBytes(path, data);

            if (!_added.TryGetValue(family, out var faces))
                _added[family] = faces = [];

            // Win2D names a font by the file and the family the file itself declares, which is not the name the document gave it.
            var address = new Uri(path).AbsoluteUri;
            faces.Add(new AddedFace(address, DeclaredFamily(address) ?? family, descriptors));
        }
    }

    private static string? DeclaredFamily(string address)
    {
        using var set = new CanvasFontSet(new Uri(address));
        foreach (var font in set.Fonts)
        {
            foreach (var name in font.FamilyNames.Values)
                return name;
        }

        return null;
    }

    /// <summary>The name DirectWrite is given for <paramref name="family"/>: a document font's file address and family, or the installed name.</summary>
    public string Resolve(string family, double weight, bool italic)
    {
        lock (_gate)
        {
            if (!_added.TryGetValue(family, out var faces) || faces.Count == 0)
                return family;

            // The face declared closest to the request; DirectWrite then picks within the file.
            var best = faces[0];
            var bestDistance = double.MaxValue;
            foreach (var face in faces)
            {
                var declared = face.Descriptors.Weight is { Minimum: > 0 } w ? w.Minimum : 400;
                var distance = Math.Abs(declared - weight) + (face.Descriptors.IsItalic == italic ? 0 : 1000);
                if (distance < bestDistance)
                {
                    best = face;
                    bestDistance = distance;
                }
            }

            return best.FileAddress + "#" + best.FamilyName;
        }
    }

    public DirectWriteFont CreateFont(string family, double size, PaintFontStyle style, double weight, double stretch) =>
        new(_context, family, size, style, weight, stretch);

    public DirectWriteFont? CreateFontForCodepoint(string family, double size, PaintFontStyle style, double weight, double stretch, System.Text.Rune codepoint)
    {
        // DirectWrite substitutes silently for a missing character, so whether a family covers it is asked of the font file itself.
        if (!_context.CoverageFontSet.TryFindFamily(family, out var typefaceFamily) || !typefaceFamily.TryMatch(PeachDrawing.Text.TypefaceQuery.From(weight, stretch, false, codepoint, null, null), out _))
            return null;

        return CreateFont(family, size, style, weight, stretch);
    }

    public DirectWriteFont? CreateSystemFallbackFont(double size, PaintFontStyle style, double weight, double stretch, System.Text.Rune codepoint)
    {
        if (!_context.CoverageFontSet.TryFindCoveringFamily(codepoint, PeachDrawing.Text.Unicode.EmojiPresentation.NoPreference, out var family))
            return null;

        return CreateFont(family.Name, size, style, weight, stretch);
    }

    private static string Extension(byte[] data) =>
        data.Length >= 4 && data[0] == 'O' && data[1] == 'T' && data[2] == 'T' && data[3] == 'O' ? ".otf" : ".ttf";

    public void Dispose()
    {
        string? folder;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            folder = _folder;
            _folder = null;
            _added.Clear();
        }

        if (folder is null)
            return;

        // Best effort, file by file: a font DirectWrite still has mapped cannot be deleted yet, and must not stop the others going.
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            Directory.Delete(folder);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
