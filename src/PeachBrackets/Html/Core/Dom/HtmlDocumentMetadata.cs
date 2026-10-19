using System;

namespace PeachPDF.Html.Core.Dom;

internal sealed record HtmlDocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    DateTime? Date,
    string? Generator
);
