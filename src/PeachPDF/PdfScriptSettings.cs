using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF
{
    /// <summary>
    /// Raised when a script sets <c>Prince.failStatus</c> to true, asking for the conversion to end without producing a PDF (a check the document
    /// makes of its own layout, such as overflowing content or too many pages, found a problem).
    /// </summary>
    public sealed class PdfFailSafeException : InvalidOperationException
    {
        /// <summary>Makes the exception.</summary>
        public PdfFailSafeException()
            : base("A script asked for the conversion to fail (Prince.failStatus was set), so no PDF was produced.")
        {
        }
    }

    /// <summary>
    /// What a document's scripts say about the PDF: the <c>PDF</c> object, which sets properties of the file (its information, how a viewer opens
    /// it, who may print it, what is attached), and <c>Prince.failStatus</c>. PeachPDF registers both with the script host through
    /// <see cref="ScriptApi"/>, so it does not depend on the package that runs the scripts. A value a script leaves alone is null here and
    /// <see cref="PdfGenerator"/> falls back on the configuration and the document.
    /// </summary>
    internal sealed class PdfScriptSettings
    {
        private string? _pageMode;
        private string? _pageLayout;
        private string? _openAction;
        private string? _printScaling;
        private string? _duplex;
        private ScriptLayout? _layout;
        private readonly List<(string Url, string? Description)> _attachments = [];

        public PdfScriptSettings()
        {
            PdfApi = BuildPdfApi();
            PrinceApi = new ScriptApi("Prince")
                .AddProperty("failStatus", () => FailRequested, value => FailRequested = value.AsBoolean());
        }

        /// <summary>The <c>PDF</c> object.</summary>
        public ScriptApi PdfApi { get; }

        /// <summary>The members PeachPDF adds to the <c>Prince</c> object.</summary>
        public ScriptApi PrinceApi { get; }

        public string? Title { get; private set; }

        public string? Subject { get; private set; }

        public string? Author { get; private set; }

        public string? Keywords { get; private set; }

        public string? Creator { get; private set; }

        public string? Language { get; private set; }

        public bool? Compress { get; private set; }

        public bool? XmpMetadata { get; private set; }

        public bool? DisplayDocTitle { get; private set; }

        public bool Encrypt { get; private set; }

        public string? UserPassword { get; private set; }

        public string? OwnerPassword { get; private set; }

        public bool AllowPrint { get; private set; } = true;

        public bool AllowModify { get; private set; } = true;

        public bool AllowCopy { get; private set; } = true;

        public bool AllowAnnotate { get; private set; } = true;

        /// <summary>Whether a script set <c>Prince.failStatus</c>.</summary>
        public bool FailRequested { get; private set; }

        public PdfPageMode? PageMode => _pageMode switch
        {
            "show-bookmarks" => PdfPageMode.UseOutlines,
            "show-thumbnails" => PdfPageMode.UseThumbs,
            "fullscreen" => PdfPageMode.FullScreen,
            "show-attachments" => PdfPageMode.UseAttachments,
            _ => null,
        };

        public PdfPageLayout? PageLayout => _pageLayout switch
        {
            "single-page" => PdfPageLayout.SinglePage,
            "one-column" => PdfPageLayout.OneColumn,
            "two-column" or "two-column-left" => PdfPageLayout.TwoColumnLeft,
            "two-column-right" => PdfPageLayout.TwoColumnRight,
            "two-page" or "two-page-left" => PdfPageLayout.TwoPageLeft,
            "two-page-right" => PdfPageLayout.TwoPageRight,
            _ => null,
        };

        public PdfOpenAction? OpenAction => _openAction switch
        {
            "print" => PdfOpenAction.Print,
            "fit-page" or "zoom(fit-page)" => PdfOpenAction.FitPage,
            "fit-width" or "zoom(fit-width)" => PdfOpenAction.FitWidth,
            _ => null,
        };

        public PdfPrintScaling? PrintScaling => _printScaling switch
        {
            "none" => PdfPrintScaling.None,
            "auto" => PdfPrintScaling.AppDefault,
            _ => null,
        };

        public PdfDuplex? Duplex => _duplex switch
        {
            "simplex" => PdfDuplex.Simplex,
            "duplex-flip-short-edge" => PdfDuplex.FlipShortEdge,
            "duplex-flip-long-edge" => PdfDuplex.FlipLongEdge,
            _ => null,
        };

        /// <summary>The encryption the script asked for, or null when it asked for none.</summary>
        public PdfEncryptionOptions? BuildEncryption()
        {
            if (!Encrypt)
                return null;

            var permissions = PdfPermissions.All;
            if (!AllowPrint) permissions &= ~(PdfPermissions.AllowPrint | PdfPermissions.AllowHighQualityPrint);
            if (!AllowModify) permissions &= ~(PdfPermissions.AllowModify | PdfPermissions.AllowAssemble);
            if (!AllowCopy) permissions &= ~(PdfPermissions.AllowCopy | PdfPermissions.AllowAccessibilityExtract);
            if (!AllowAnnotate) permissions &= ~(PdfPermissions.AllowAnnotations | PdfPermissions.AllowFillForms);

            return new PdfEncryptionOptions { UserPassword = UserPassword, OwnerPassword = OwnerPassword, Permissions = permissions };
        }

        /// <summary>The files the script asked to attach, read through <paramref name="load"/>.</summary>
        public async Task<List<PdfAttachment>> LoadAttachmentsAsync(Func<string, Task<byte[]?>> load)
        {
            var files = new List<PdfAttachment>();
            foreach (var (url, description) in _attachments)
            {
                var data = await load(url)
                    ?? throw new InvalidOperationException($"PDF.attachFile could not read '{url}'.");
                files.Add(new PdfAttachment
                {
                    FileName = FileNameOf(url),
                    Data = data,
                    Description = description,
                    MimeType = "application/octet-stream",
                });
            }

            return files;
        }

        // The name a file is attached under: the last part of its address, or "attachment" when it has none (a data: address).
        private static string FileNameOf(string url)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Scheme == "data" ? string.Empty : uri.LocalPath) : url;
            var name = System.IO.Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? "attachment" : name;
        }

        /// <summary>Throws <see cref="PdfFailSafeException"/> when a script asked for the conversion to fail.</summary>
        public void ThrowIfFailed()
        {
            if (FailRequested)
                throw new PdfFailSafeException();
        }

        private ScriptApi BuildPdfApi()
        {
            var api = new ScriptApi("PDF")
                .AddProperty("title", () => Title, v => Title = Text(v))
                .AddProperty("subject", () => Subject, v => Subject = Text(v))
                .AddProperty("author", () => Author, v => Author = Text(v))
                .AddProperty("keywords", () => Keywords, v => Keywords = Text(v))
                .AddProperty("creator", () => Creator, v => Creator = Text(v))
                .AddProperty("lang", () => Language, v => Language = Text(v))
                .AddProperty("compress", () => Compress ?? true, v => Compress = v.AsBoolean())
                .AddProperty("xmpMetadata", () => XmpMetadata ?? false, v => XmpMetadata = v.AsBoolean())
                .AddProperty("displayDocTitle", () => DisplayDocTitle ?? false, v => DisplayDocTitle = v.AsBoolean())
                .AddProperty("encrypt", () => Encrypt, v => Encrypt = v.AsBoolean())
                .AddProperty("userPassword", () => UserPassword, v => UserPassword = Text(v))
                .AddProperty("ownerPassword", () => OwnerPassword, v => OwnerPassword = Text(v))
                .AddProperty("allowPrint", () => AllowPrint, v => AllowPrint = v.AsBoolean())
                .AddProperty("allowModify", () => AllowModify, v => AllowModify = v.AsBoolean())
                .AddProperty("allowCopy", () => AllowCopy, v => AllowCopy = v.AsBoolean())
                .AddProperty("allowAnnotate", () => AllowAnnotate, v => AllowAnnotate = v.AsBoolean())
                .AddProperty("keyBits", () => "256", v => RequireKeyBits(v))
                .AddProperty("pageMode", () => _pageMode ?? "auto", v => _pageMode = Choice(v, "pageMode", "auto", "show-bookmarks", "show-thumbnails", "fullscreen", "show-attachments"))
                .AddProperty("pageLayout", () => _pageLayout ?? "auto", v => _pageLayout = Choice(v, "pageLayout", "auto", "single-page", "one-column", "two-column", "two-column-left", "two-column-right", "two-page", "two-page-left", "two-page-right"))
                .AddProperty("openAction", () => _openAction, v => _openAction = Choice(v, "openAction", "print", "fit-page", "fit-width", "zoom(fit-page)", "zoom(fit-width)"))
                .AddProperty("printScaling", () => _printScaling ?? "auto", v => _printScaling = Choice(v, "printScaling", "auto", "none"))
                .AddProperty("duplex", () => _duplex ?? "auto", v => _duplex = Choice(v, "duplex", "auto", "simplex", "duplex-flip-short-edge", "duplex-flip-long-edge"))
                .AddProperty("pages", PagesValue)
                .AddMethod("attachFile", args =>
                {
                    if (args.Count == 0 || string.IsNullOrEmpty(args[0].AsString()))
                        throw new ArgumentException("PDF.attachFile needs the address of a file.");

                    _attachments.Add((args[0].AsString()!, args.Count > 1 ? args[1].AsString() : null));
                    return ScriptValue.Undefined;
                });

            api.LayoutCompleted += layout => _layout = layout;
            return api;
        }

        private static string? Text(ScriptValue value) => value.IsNullish ? null : value.AsString();

        // A property that takes one of a few names refuses any other, as a script would find out as soon as it ran.
        private static string? Choice(ScriptValue value, string property, params string[] allowed)
        {
            var text = Text(value)?.Trim().ToLowerInvariant();
            if (text is null || text == "auto" && allowed.Contains("auto"))
                return null;
            if (!allowed.Contains(text))
                throw new ArgumentException($"PDF.{property} cannot be '{value.AsString()}'; it is one of {string.Join(", ", allowed)}.");

            return text;
        }

        // The encryption is AES-256, whatever the script thinks of 40 and 128 bit keys.
        private static void RequireKeyBits(ScriptValue value)
        {
            if (value.AsString() != "256" && value.AsNumber() != 256)
                throw new ArgumentException("PDF.keyBits can only be 256: PeachPDF encrypts with AES-256 and does not write the weaker 40 and 128 bit formats.");
        }

        // The boxes of the last layout, as plain values: pages, each a tree of boxes with their place and size. Null until the document is laid out.
        private ScriptValue PagesValue()
        {
            if (_layout is not { } layout)
                return ScriptValue.Null;

            return ScriptValue.From(layout.Pages.Select(BoxValue).ToList());
        }

        private static ScriptValue BoxValue(BoxReport box)
        {
            var properties = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["type"] = box.Kind switch
                {
                    BoxReportKind.Page => "BODY",
                    BoxReportKind.Footnotes => "FOOTNOTES",
                    BoxReportKind.Span => "SPAN",
                    BoxReportKind.Text => "TEXT",
                    BoxReportKind.Image => "IMAGE",
                    BoxReportKind.Svg => "SVG",
                    _ => "BOX",
                },
                ["pageNum"] = box.PageNumber,
                ["x"] = box.X,
                ["y"] = box.PageHeight - (box.Y + box.Height),
                ["w"] = box.Width,
                ["h"] = box.Height,
                ["text"] = box.Text ?? string.Empty,
                ["pseudo"] = box.PseudoElement,
                ["src"] = box.ImageSource,
                ["children"] = ScriptValue.From(box.Children.Select(BoxValue).ToList()),
            };

            if (box.Element is { } element)
                properties["element"] = ScriptValue.From(element);

            return ScriptValue.From(properties);
        }
    }
}
