// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Makes byte-identical image XObjects of a document share one object, so the file carries each image once.
/// </summary>
internal static class PdfImageConsolidator
{
    /// <summary>Points every page's reference to an image at the first object with the same <see cref="ImageIdentity"/>.</summary>
    public static void Consolidate(PdfDocument document)
    {
        var images = ConsolidationCandidate.FindAll(document);

        var mapXObjectToIdentity = new Dictionary<PdfDictionary, ImageIdentity>(ReferenceEqualityComparer.Instance);
        var mapIdentityToPdfItem = new Dictionary<ImageIdentity, PdfItem>();

        // Work out an identity for each image XObject and build lookups for all images.
        foreach (ConsolidationCandidate img in images)
        {
            mapXObjectToIdentity[img.XObject] = img.Identity;
            mapIdentityToPdfItem[img.Identity] = img.Item.Value;
        }

        // Set the PdfItem for each image to the one chosen for its identity.
        foreach (ConsolidationCandidate img in images)
        {
            img.XObjects.Elements[img.Item.Key] = mapIdentityToPdfItem[mapXObjectToIdentity[img.XObject]];
        }
    }
}

/// <summary>
/// Everything that affects how an image XObject renders: its content-stream bytes, the dictionary attributes
/// that give those bytes meaning (/ColorSpace, /Width, /Height, /BitsPerComponent, /Decode, /Interpolate - the
/// same attributes <see cref="Advanced.PdfImageTable"/>'s own embed-time selector treats as part of an image's
/// identity) and any /SMask (recursively, since the mask is itself an image XObject that can differ the same
/// way). Two images are safe to consolidate only when the whole identity matches - matching content stream
/// bytes alone is not enough, since e.g. the same base color stream with and without an /SMask must not be
/// merged, or one page silently loses (or gains) transparency.
/// <para>
/// Equality compares the bytes themselves rather than a digest of them, so two different images can never be
/// taken for one another, and nothing here depends on a hash algorithm the host may not permit.
/// </para>
/// </summary>
internal sealed class ImageIdentity : IEquatable<ImageIdentity>
{
    // How many bytes at each end of a stream feed the bucket hash; equality still compares every byte.
    private const int HashedBytes = 64;

    private readonly byte[] _data;
    private readonly string _attributes;
    private readonly List<byte[]> _referencedStreams;
    private readonly ImageIdentity? _softMask;

    private ImageIdentity(byte[] data, string attributes, List<byte[]> referencedStreams, ImageIdentity? softMask)
    {
        _data = data;
        _attributes = attributes;
        _referencedStreams = referencedStreams;
        _softMask = softMask;
    }

    public static ImageIdentity Of(PdfDictionary xObject)
    {
        var referencedStreams = new List<byte[]>();
        var sb = new StringBuilder();
        sb.Append(DescribeItem(xObject.Elements["/ColorSpace"], referencedStreams));
        sb.Append('|').Append(DescribeItem(xObject.Elements["/Width"], referencedStreams));
        sb.Append('|').Append(DescribeItem(xObject.Elements["/Height"], referencedStreams));
        sb.Append('|').Append(DescribeItem(xObject.Elements["/BitsPerComponent"], referencedStreams));
        sb.Append('|').Append(DescribeItem(xObject.Elements["/Decode"], referencedStreams));
        sb.Append('|').Append(DescribeItem(xObject.Elements["/Interpolate"], referencedStreams));

        var softMask = xObject.Elements.GetDictionary("/SMask");
        return new ImageIdentity(
            xObject.Stream?.Value ?? [],
            sb.ToString(),
            referencedStreams,
            softMask is not null ? Of(softMask) : null);
    }

    /// <summary>
    /// Renders an arbitrary dictionary-value PdfItem (a /ColorSpace name, an /Indexed or /ICCBased
    /// array, a plain scalar, or an indirect reference to any of these) into a string that differs
    /// whenever the referenced content differs. Dereferences through PdfReference and recurses into
    /// array elements and referenced streams so two distinct objects that describe the same content
    /// (e.g. two indirect but byte-identical ICC profiles) still compare equal, rather than being
    /// compared by object identity. A referenced stream's bytes are what matters for identity, not
    /// which object number holds them, so they are collected into <paramref name="streams"/> in the
    /// order met and stood for in the text by their position there.
    /// </summary>
    private static string DescribeItem(PdfItem? item, List<byte[]> streams)
    {
        switch (item)
        {
            case null:
                return "«null»";
            case PdfReference reference:
                return DescribeItem(reference.Value, streams);
            case PdfArray array:
                return "[" + string.Join(",", array.Select(element => DescribeItem(element, streams))) + "]";
            case PdfDictionary dict:
                if (dict.Stream is null)
                    return "«dict»";
                streams.Add(dict.Stream.Value);
                return "«stream#" + (streams.Count - 1) + "»";
            default:
                return item.ToString() ?? "«null»";
        }
    }

    public bool Equals(ImageIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || _attributes != other._attributes
            || _referencedStreams.Count != other._referencedStreams.Count
            || !_data.AsSpan().SequenceEqual(other._data))
            return false;

        for (int i = 0; i < _referencedStreams.Count; i++)
        {
            if (!_referencedStreams[i].AsSpan().SequenceEqual(other._referencedStreams[i]))
                return false;
        }

        return Equals(_softMask, other._softMask);
    }

    public override bool Equals(object? obj) => obj is ImageIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_attributes);
        hash.Add(_data.Length);
        hash.AddBytes(_data.AsSpan(0, Math.Min(HashedBytes, _data.Length)));
        hash.AddBytes(_data.AsSpan(Math.Max(0, _data.Length - HashedBytes)));
        hash.Add(_softMask);
        return hash.ToHashCode();
    }
}

internal sealed class ConsolidationCandidate
{
    public PdfDictionary XObjects { get; }
    public KeyValuePair<string, PdfItem> Item { get; }
    public PdfDictionary XObject { get; }
    public ImageIdentity Identity { get; }

    public ConsolidationCandidate(PdfDictionary xObjects, KeyValuePair<string, PdfItem> item, PdfDictionary xObject)
    {
        XObjects = xObjects;
        Item = item;
        XObject = xObject;
        Identity = ImageIdentity.Of(xObject);
    }

    /// <summary>
    /// Get info for each image in the document.
    /// </summary>
    internal static List<ConsolidationCandidate> FindAll(PdfDocument doc) =>
        [.. doc.Pages.Cast<PdfPage>()
            .Select(page => page.Elements.GetDictionary("/Resources"))
            .Select(resources => resources?.Elements?.GetDictionary("/XObject"))
            .Where(xObjects => xObjects?.Elements is not null)
            .SelectMany(xObjects =>
                from item in xObjects!.Elements
                let xObject = (item.Value as PdfReference)?.Value as PdfDictionary
                where xObject?.Elements?.GetString("/Subtype") == "/Image"
                select new ConsolidationCandidate(xObjects, item, xObject)
            )];
}
