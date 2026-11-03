using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeachBrackets.Updates;

/// <summary>A file a host's picker returned for a file input: its name, kind and bytes.</summary>
/// <param name="Name">the file name, without a path</param>
/// <param name="ContentType">the media type, or empty when it is not known (it is sent as <c>application/octet-stream</c>)</param>
/// <param name="Content">what the file holds</param>
public sealed record ChosenFile(string Name, string ContentType, byte[] Content);

/// <summary>One name and value of a form being submitted: a string, or - for a file input - a file.</summary>
/// <param name="Name">the control's name</param>
/// <param name="Value">the value, with line breaks as CRLF; null for a file</param>
/// <param name="File">the file, or null for a string</param>
public readonly record struct FormEntry(string Name, string? Value, ChosenFile? File);

/// <summary>
/// A form as it is submitted: where to, how, and the name and value of each control that takes part (<see cref="HtmlDocument.CreateFormSubmission"/>). The
/// document does not send it; a host or application does, with the body <see cref="Serialize"/> makes or, for <c>get</c>, the address <see cref="GetUri"/> makes.
/// </summary>
public sealed class FormSubmission
{
    internal FormSubmission(string method, string action, string encType, IReadOnlyList<FormEntry> entries)
    {
        Method = method;
        Action = action;
        EncType = encType;
        Entries = entries;
    }

    /// <summary>The method: <c>get</c>, <c>post</c> or <c>dialog</c> (which sends nothing).</summary>
    public string Method { get; }

    /// <summary>The address to send it to, resolved against the document's base; empty when the form has no <c>action</c>.</summary>
    public string Action { get; }

    /// <summary>How the body is encoded: <c>application/x-www-form-urlencoded</c>, <c>multipart/form-data</c> or <c>text/plain</c>.</summary>
    public string EncType { get; }

    /// <summary>The controls that take part, in document order.</summary>
    public IReadOnlyList<FormEntry> Entries { get; }

    /// <summary>
    /// The address a <c>get</c> submission goes to: the action with its query replaced by the entries (a file contributes its name). For any other method it is
    /// the action as it is.
    /// </summary>
    /// <returns>the address</returns>
    public string GetUri()
    {
        if (!Method.Equals("get", StringComparison.OrdinalIgnoreCase))
            return Action;

        var hash = Action.IndexOf('#', StringComparison.Ordinal);
        var withoutFragment = hash >= 0 ? Action[..hash] : Action;
        var fragment = hash >= 0 ? Action[hash..] : string.Empty;
        var question = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        var path = question >= 0 ? withoutFragment[..question] : withoutFragment;
        return path + "?" + UrlEncoded() + fragment;
    }

    /// <summary>The body of a submission, in the encoding the form asks for.</summary>
    /// <returns>the <c>Content-Type</c> header value (with the boundary, for multipart) and the bytes; empty for <c>dialog</c></returns>
    public (string ContentType, byte[] Body) Serialize()
    {
        if (Method.Equals("dialog", StringComparison.OrdinalIgnoreCase))
            return (EncType, []);

        return EncType switch
        {
            "multipart/form-data" => Multipart(),
            "text/plain" => ("text/plain", Encoding.UTF8.GetBytes(string.Concat(Entries.Select(e => e.Name + "=" + (e.Value ?? e.File?.Name ?? string.Empty) + "\r\n")))),
            _ => ("application/x-www-form-urlencoded", Encoding.ASCII.GetBytes(UrlEncoded())),
        };
    }

    private string UrlEncoded() =>
        string.Join('&', Entries.Select(e => Encode(e.Name) + "=" + Encode(e.Value ?? e.File?.Name ?? string.Empty)));

    // https://url.spec.whatwg.org/#concept-urlencoded-byte-serializer: a space is a plus, and everything but letters, digits and *-._ is a percent sign and two hex digits.
    private static string Encode(string text)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if (b == ' ')
                builder.Append('+');
            else if (b < 128 && (char.IsAsciiLetterOrDigit(c) || c is '*' or '-' or '.' or '_'))
                builder.Append(c);
            else
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private (string ContentType, byte[] Body) Multipart()
    {
        var boundary = "----PeachPDFFormBoundary" + Guid.NewGuid().ToString("N");
        using var body = new System.IO.MemoryStream();

        void Write(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            body.Write(bytes, 0, bytes.Length);
        }

        foreach (var entry in Entries)
        {
            Write("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + Escape(entry.Name) + "\"");
            if (entry.File is { } file)
            {
                Write("; filename=\"" + Escape(file.Name) + "\"\r\nContent-Type: " + (file.ContentType.Length > 0 ? file.ContentType : "application/octet-stream") + "\r\n\r\n");
                body.Write(file.Content, 0, file.Content.Length);
                Write("\r\n");
            }
            else
            {
                Write("\r\n\r\n" + entry.Value + "\r\n");
            }
        }

        Write("--" + boundary + "--\r\n");
        return ("multipart/form-data; boundary=" + boundary, body.ToArray());
    }

    // A name in a Content-Disposition header: a quote, a carriage return and a line feed are escaped.
    private static string Escape(string name) =>
        name.Replace("\"", "%22", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
}
