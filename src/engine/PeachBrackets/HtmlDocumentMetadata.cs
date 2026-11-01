using System;

namespace PeachBrackets;

/// <summary>
/// What the document says about itself: the <c>&lt;title&gt;</c> and the <c>&lt;meta&gt;</c> elements a reader of the page could show
/// in a properties dialog.
/// </summary>
/// <param name="Title">The <c>&lt;title&gt;</c> text, or null if the document has none.</param>
/// <param name="Author">The <c>author</c> meta element, or null.</param>
/// <param name="Subject">The <c>subject</c> (or <c>description</c>) meta element, or null.</param>
/// <param name="Keywords">The <c>keywords</c> meta element, or null.</param>
/// <param name="Date">The <c>date</c> meta element, or null when absent or unparseable.</param>
/// <param name="Generator">The <c>generator</c> meta element, naming the software that made the document, or null.</param>
public sealed record HtmlDocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    DateTime? Date,
    string? Generator
);
