namespace PeachBrackets;

/// <summary>An image of a <c>srcset</c> or <c>&lt;picture&gt;</c> that could not be loaded, see <see cref="HtmlDocument.ImageLoadFailures"/>.</summary>
/// <param name="Url">the image URL as written in the document</param>
/// <param name="FallbackUrl">the next-best image the document tried in its place, or <see langword="null"/> when there was none left to try</param>
public sealed record ImageLoadFailure(string Url, string? FallbackUrl);
