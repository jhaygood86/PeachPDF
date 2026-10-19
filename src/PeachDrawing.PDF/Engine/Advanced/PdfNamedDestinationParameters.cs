// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Internal;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Creates the named destination parameters.
/// </summary>
internal class PdfNamedDestinationParameters
{
    readonly string _parameters;

    PdfNamedDestinationParameters(string parameters)
    {
        _parameters = parameters;
    }

    static PdfNamedDestinationParameters CreateXYZ(double? left, double? top, double? zoom)
    {
        return new PdfNamedDestinationParameters(Format("/XYZ {0} {1} {2}", left, top, zoom));
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the desired left and top value and the optional zoom value on the destination page. Null values are retained unchanged.
    /// </summary>
    /// <param name="left">The left value of the displayed area in PDF world space units.</param>
    /// <param name="top">The top value of the displayed area in PDF world space units.</param>
    /// <param name="zoom">Optional: The zoom value for the displayed area. 1 = 100%, 2 = 200% etc.</param>
    public static PdfNamedDestinationParameters CreatePosition(double? left, double? top, double? zoom = null)
    {
        return CreateXYZ(left, top, zoom);
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the desired left and top value and the optional zoom value on the destination page. Null values are retained unchanged.
    /// </summary>
    /// <param name="position">An PaintPoint defining the left and top value of the displayed area in PDF world space units.</param>
    /// <param name="zoom">Optional: The zoom value for the displayed area. 1 = 100%, 2 = 200% etc.</param>
    public static PdfNamedDestinationParameters CreatePosition(PaintPoint position, double? zoom = null)
    {
        return CreateXYZ(position.X, position.Y, zoom);
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the desired top value on the destination page.
    /// The page width is fitted to the window. Null values are retained unchanged.
    /// </summary>
    /// <param name="top">The top value of the displayed area in PDF world space units.</param>
    public static PdfNamedDestinationParameters CreateFitHorizontally(double? top)
    {
        return new PdfNamedDestinationParameters(Format("/FitH {0}", top));
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the desired left value on the destination page.
    /// The page height is fitted to the window. Null values are retained unchanged.
    /// </summary>
    /// <param name="left">The left value of the displayed area in PDF world space units.</param>
    public static PdfNamedDestinationParameters CreateFitVertically(double? left)
    {
        return new PdfNamedDestinationParameters(Format("/FitV {0}", left));
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the destination page. The given rectangle is fitted to the window.
    /// </summary>
    /// <param name="left">The left value of the rectangle to display in PDF world space units.</param>
    /// <param name="top">The top value of the rectangle to display in PDF world space units.</param>
    /// <param name="right">The right value of the rectangle to display in PDF world space units.</param>
    /// <param name="bottom">The bottom value of the rectangle to display in PDF world space units.</param>
    public static PdfNamedDestinationParameters CreateFitRectangle(double left, double top, double right, double bottom)
    {
        return new PdfNamedDestinationParameters(Format("/FitR {0} {1} {2} {3}", left, bottom, right, top));
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the destination page. The given rectangle is fitted to the window.
    /// </summary>
    /// <param name="rect">The Rect representing the rectangle to display in PDF world space units.</param>
    public static PdfNamedDestinationParameters CreateFitRectangle(Rect rect)
    {
        return CreateFitRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    /// <summary>
    /// Creates a PdfNamedDestinationParameters object for a named destination.
    /// Moving to this destination will move to the destination page. The given rectangle is fitted to the window.
    /// </summary>
    /// <param name="point1">The first PaintPoint representing the rectangle to display in PDF world space units.</param>
    /// <param name="point2">The second PaintPoint representing the rectangle to display in PDF world space units.</param>
    public static PdfNamedDestinationParameters CreateFitRectangle(PaintPoint point1, PaintPoint point2)
    {
        double left = System.Math.Min(point1.X, point2.X);
        double top = System.Math.Min(point1.Y, point2.Y);
        return CreateFitRectangle(new Rect(left, top, System.Math.Max(point1.X, point2.X) - left, System.Math.Max(point1.Y, point2.Y) - top));
    }

    static string Format(string format, params double?[] values)
    {
        int length = values.Length;
        object[] objValues = new object[length];
        for (int i = 0; i < length; i++)
        {
            objValues[i] = values[i] ?? (object)"null";
        }

        return PdfEncoders.Format(format, objValues);
    }

    /// <summary>
    /// Returns the parameters string for the named destination.
    /// </summary>
    public override string ToString() => _parameters;
}
