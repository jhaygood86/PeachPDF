using System;

namespace PeachBrackets.Html.Dom;

internal sealed record HtmlDocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    DateTime? Date,
    string? Generator
);
