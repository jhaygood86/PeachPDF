using PeachBrackets.Svg;
using PeachDrawing.Core;
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace PeachBrackets.Building;

/// <summary>
/// A parsed SVG image, ready to place in a document as a vector picture.
/// </summary>
public sealed class SvgImage
{
    internal SvgImage(SvgDocument document)
    {
        Document = document;
    }

    internal SvgDocument Document { get; }

    /// <summary>
    /// Parses SVG markup.
    /// </summary>
    /// <param name="svgMarkup">an XML document whose root is an <c>&lt;svg&gt;</c> element</param>
    /// <param name="renderContext">the context that resolves the fonts and colours the image uses</param>
    /// <returns>the parsed image</returns>
    /// <exception cref="System.Xml.XmlException">the markup is not well-formed XML</exception>
    public static SvgImage Parse(string svgMarkup, RenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(svgMarkup);
        ArgumentNullException.ThrowIfNull(renderContext);

        return new SvgImage(SvgTreeBuilder.Build(new XElementSvgSourceNode(XElement.Parse(svgMarkup)), renderContext));
    }

    /// <summary>
    /// Parses SVG markup from bytes (UTF-8, or whatever a byte-order mark says).
    /// </summary>
    /// <param name="svgBytes">the encoded SVG document</param>
    /// <param name="renderContext">the context that resolves the fonts and colours the image uses</param>
    /// <returns>the parsed image</returns>
    /// <exception cref="System.Xml.XmlException">the markup is not well-formed XML</exception>
    public static SvgImage Parse(byte[] svgBytes, RenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(svgBytes);

        using var reader = new StreamReader(new MemoryStream(svgBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd(), renderContext);
    }
}
