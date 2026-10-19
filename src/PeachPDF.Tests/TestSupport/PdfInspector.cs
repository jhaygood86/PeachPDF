using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>One value of a parsed PDF: a name, string, number, boolean, null, array, dictionary, stream or indirect reference.</summary>
    internal abstract class PdfValue;

    internal sealed class PdfNull : PdfValue
    {
        public static readonly PdfNull Instance = new();
    }

    internal sealed class PdfBoolean(bool value) : PdfValue
    {
        public bool Value { get; } = value;
    }

    internal sealed class PdfNumber(double value) : PdfValue
    {
        public double Value { get; } = value;
    }

    /// <summary>A name object; <see cref="Value"/> includes the leading slash, the way the file spells it.</summary>
    internal sealed class PdfName(string value) : PdfValue
    {
        public string Value { get; } = value;

        public override string ToString() => Value;
    }

    internal sealed class PdfText(byte[] bytes) : PdfValue
    {
        public byte[] Bytes { get; } = bytes;

        /// <summary>The string as PDF text: UTF-16BE with a byte-order mark, or one byte per character otherwise.</summary>
        public string Value => Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF
            ? Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2)
            : Encoding.Latin1.GetString(Bytes);

        public override string ToString() => Value;
    }

    internal sealed class PdfArr : PdfValue
    {
        public PdfArr(PdfFile file, List<PdfValue> items)
        {
            File = file;
            Items = items;
        }

        public PdfFile File { get; }

        public List<PdfValue> Items { get; }

        public int Count => Items.Count;

        /// <summary>The element at <paramref name="index"/>, with an indirect reference followed.</summary>
        public PdfValue this[int index] => File.Resolve(Items[index]);

        public IEnumerable<PdfValue> Resolved => Items.Select(File.Resolve);

        /// <summary>The array as PDF syntax would spell it without the brackets - <c>4 0 R /FitH 755</c> - for a test that matches a pattern in it.</summary>
        public string Render() => string.Join(" ", Items.Select(Render));

        private static string Render(PdfValue value) => value switch
        {
            PdfRef r => $"{r.Number} 0 R",
            PdfNumber n => n.Value.ToString("0.####", CultureInfo.InvariantCulture),
            PdfName name => name.Value,
            PdfText text => $"({text.Value})",
            PdfBoolean b => b.Value ? "true" : "false",
            PdfArr a => $"[{a.Render()}]",
            _ => "null",
        };
    }

    /// <summary>An indirect reference; <see cref="Number"/> is the object number it points at.</summary>
    internal sealed class PdfRef(int number) : PdfValue
    {
        public int Number { get; } = number;
    }

    internal class PdfDict : PdfValue
    {
        /// <summary>The object number when this dictionary is an indirect object; two views of the same object agree on it.</summary>
        public int ObjectNumber { get; internal set; }

        public PdfDict(PdfFile file, Dictionary<string, PdfValue> entries)
        {
            File = file;
            Entries = entries;
        }

        public PdfFile File { get; }

        public Dictionary<string, PdfValue> Entries { get; }

        public bool ContainsKey(string key) => Entries.ContainsKey(key);

        public IEnumerable<string> Keys => Entries.Keys;

        /// <summary>The value under <paramref name="key"/> with an indirect reference followed, or null when absent.</summary>
        public PdfValue? this[string key] => Entries.TryGetValue(key, out var value) ? File.Resolve(value) : null;

        /// <summary>The raw (unresolved) value, so a test can tell a reference from an inline object.</summary>
        public PdfValue? Raw(string key) => Entries.GetValueOrDefault(key);

        /// <summary>The object number the entry references, or null when it is absent or inline.</summary>
        public int? RefNumber(string key) => Entries.GetValueOrDefault(key) is PdfRef r ? r.Number : null;

        public PdfDict? Dict(string key) => this[key] as PdfDict;

        public PdfArr? Array(string key) => this[key] as PdfArr;

        public string? Name(string key) => (this[key] as PdfName)?.Value;

        public string? Text(string key) => (this[key] as PdfText)?.Value;

        public double? Number(string key) => (this[key] as PdfNumber)?.Value;

        public bool? Bool(string key) => (this[key] as PdfBoolean)?.Value;

        /// <summary>A PDF date string (<c>D:YYYYMMDDHHmmSS...</c>) as an instant.</summary>
        public DateTimeOffset? Date(string key)
        {
            var text = Text(key);
            if (text is null)
                return null;

            var match = Regex.Match(text, @"^D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?(\d{2})?(?:([Zz+\-])(?:(\d{2})'?(\d{2})?'?)?)?");
            if (!match.Success)
                return null;

            int Part(int group, int fallback) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : fallback;

            var local = new DateTime(Part(1, 1970), Part(2, 1), Part(3, 1), Part(4, 0), Part(5, 0), Part(6, 0));
            var sign = match.Groups[7].Success ? match.Groups[7].Value : "Z";
            var offset = sign is "Z" or "z"
                ? TimeSpan.Zero
                : new TimeSpan(Part(8, 0), Part(9, 0), 0) * (sign == "-" ? -1 : 1);
            return new DateTimeOffset(local, offset);
        }
    }

    internal sealed class PdfStreamObject(PdfFile file, Dictionary<string, PdfValue> entries, byte[] raw) : PdfDict(file, entries)
    {
        /// <summary>The bytes as stored, still filtered.</summary>
        public byte[] StoredBytes { get; } = raw;

        /// <summary>The stream data with its <c>/Filter</c> chain undone (Flate and Brotli; anything else is left as stored).</summary>
        public byte[] Decoded
        {
            get
            {
                var filters = this["/Filter"] switch
                {
                    PdfName name => [name.Value],
                    PdfArr array => array.Resolved.OfType<PdfName>().Select(n => n.Value).ToList(),
                    _ => new List<string>(),
                };

                var data = StoredBytes;
                foreach (var filter in filters)
                {
                    if (filter is "/FlateDecode" or "/Fl")
                    {
                        using var input = new MemoryStream(data, 2, data.Length - 2);
                        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
                        using var output = new MemoryStream();
                        inflate.CopyTo(output);
                        data = output.ToArray();
                    }
                    else if (filter == "/BrotliDecode")
                    {
                        using var input = new MemoryStream(data);
                        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
                        using var output = new MemoryStream();
                        brotli.CopyTo(output);
                        data = output.ToArray();
                    }
                }

                return data;
            }
        }

        public string DecodedText => Encoding.Latin1.GetString(Decoded);
    }

    /// <summary>
    /// A saved PDF read back from its bytes: the object table, and the document-level views a test asserts on (pages, outlines,
    /// catalog, structure tree, form fields). PeachPDF's PDF writer is write-only and keeps its object model internal, so a test that
    /// wants to assert on structure rather than grep tokens reads the file the way any consumer would.
    /// </summary>
    internal sealed class PdfFile
    {
        private readonly Dictionary<int, PdfValue> _objects = [];
        private List<PdfDict>? _pages;

        private PdfFile(byte[] bytes)
        {
            Bytes = bytes;
        }

        public byte[] Bytes { get; }

        public PdfDict Trailer { get; private set; } = null!;

        /// <summary>The header version, such as <c>1.7</c> or <c>2.0</c>.</summary>
        public string Version { get; private set; } = "";

        public static PdfFile Parse(byte[] bytes)
        {
            var file = new PdfFile(bytes);
            file.ReadAll();
            return file;
        }

        public static PdfFile Read(PeachPdfDocument document)
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            return Parse(stream.ToArray());
        }

        public PdfValue Resolve(PdfValue value)
        {
            // Bounded, so a malformed self-referencing chain cannot loop a test forever.
            for (var hops = 0; value is PdfRef r && hops < 32; hops++)
                value = _objects.TryGetValue(r.Number, out var target) ? target : PdfNull.Instance;
            return value;
        }

        public PdfValue? Object(int number) => _objects.GetValueOrDefault(number);

        public IEnumerable<KeyValuePair<int, PdfValue>> Objects => _objects;

        public PdfDict Catalog => (PdfDict)Resolve(Trailer.Raw("/Root") ?? PdfNull.Instance);

        /// <summary>The document information dictionary (<c>/Info</c>), or an empty one when the file has none.</summary>
        public PdfDict Info => Trailer.Dict("/Info") ?? new PdfDict(this, []);

        /// <summary>The catalog's <c>/Lang</c> entry.</summary>
        public string? Language => Catalog.Text("/Lang");

        public IReadOnlyList<PdfDict> Pages => _pages ??= CollectPages();

        public int PageCount => Pages.Count;

        /// <summary>The page's width in points, from its <c>/MediaBox</c>.</summary>
        public static double Width(PdfDict page) => Box(page)[2] - Box(page)[0];

        /// <summary>The page's height in points, from its <c>/MediaBox</c>.</summary>
        public static double Height(PdfDict page) => Box(page)[3] - Box(page)[1];

        private static double[] Box(PdfDict page)
        {
            var box = page.Array("/MediaBox") ?? (PdfArr?)Inherited(page, "/MediaBox");
            return box is null ? [0, 0, 0, 0] : box.Resolved.Select(v => ((PdfNumber)v).Value).ToArray();
        }

        private static PdfValue? Inherited(PdfDict page, string key)
        {
            for (var node = page; node is not null; node = node.Dict("/Parent"))
            {
                if (node[key] is { } value)
                    return value;
            }

            return null;
        }

        /// <summary>The decoded content of every stream of a page, in order.</summary>
        public IReadOnlyList<PdfStreamObject> ContentStreams(PdfDict page) => page["/Contents"] switch
        {
            PdfStreamObject single => [single],
            PdfArr array => array.Resolved.OfType<PdfStreamObject>().ToList(),
            _ => [],
        };

        /// <summary>A page's content stream text, the streams joined in order.</summary>
        public string ContentText(PdfDict page) => string.Concat(ContentStreams(page).Select(s => s.DecodedText));

        /// <summary>The annotation dictionaries of a page.</summary>
        public IReadOnlyList<PdfDict> Annotations(PdfDict page) =>
            page.Array("/Annots")?.Resolved.OfType<PdfDict>().ToList() ?? [];

        /// <summary>The zero-based index of the page <paramref name="target"/> refers to, or -1.</summary>
        public int PageIndexOf(PdfValue? target)
        {
            if (target is null)
                return -1;

            var resolved = Resolve(target);
            for (var i = 0; i < Pages.Count; i++)
            {
                if (ReferenceEquals(Pages[i], resolved))
                    return i;
            }

            return -1;
        }

        private List<PdfDict> CollectPages()
        {
            var result = new List<PdfDict>();
            void Walk(PdfDict node)
            {
                if (node.Array("/Kids") is { } kids)
                {
                    foreach (var kid in kids.Resolved.OfType<PdfDict>())
                        Walk(kid);
                }
                else
                {
                    result.Add(node);
                }
            }

            if (Catalog.Dict("/Pages") is { } root)
                Walk(root);
            return result;
        }

        /// <summary>One entry of the outline tree.</summary>
        public sealed class Outline(PdfFile file, PdfDict dict)
        {
            public PdfDict Dict { get; } = dict;

            public string Title => Dict.Text("/Title") ?? "";

            /// <summary>The page the entry jumps to, or -1 when it does not target a page.</summary>
            public int DestinationPageIndex => file.PageIndexOf(Destination()?.FirstOrDefault());

            /// <summary>Whether the entry is expanded: a positive <c>/Count</c>. Only an entry with children carries one.</summary>
            public bool Opened => Dict.Number("/Count") > 0;

            public bool HasDestinationPage => DestinationPageIndex >= 0;

            /// <summary>The destination's vertical position in PDF user space (bottom-up), from a <c>/XYZ</c> or <c>/FitH</c> destination.</summary>
            public double? DestinationTop
            {
                get
                {
                    var items = Destination();
                    if (items is null || items.Count < 3)
                        return null;
                    var mode = (items[1] as PdfName)?.Value;
                    var index = mode == "/XYZ" ? 3 : mode is "/FitH" or "/FitBH" ? 2 : -1;
                    return index >= 0 && index < items.Count ? (items[index] as PdfNumber)?.Value : null;
                }
            }

            public string? Url => Dict.Dict("/A")?.Text("/URI");

            public IReadOnlyList<Outline> Children => file.OutlineChildren(Dict);

            private List<PdfValue>? Destination()
            {
                var dest = Dict["/Dest"] ?? Dict.Dict("/A")?["/D"];
                return dest is PdfArr array ? array.Items : null;
            }
        }

        /// <summary>The top-level outline entries.</summary>
        public IReadOnlyList<Outline> Outlines => Catalog.Dict("/Outlines") is { } root ? OutlineChildren(root) : [];

        private List<Outline> OutlineChildren(PdfDict parent)
        {
            var result = new List<Outline>();
            for (var node = parent.Dict("/First"); node is not null; node = node.Dict("/Next"))
                result.Add(new Outline(this, node));
            return result;
        }

        /// <summary>Looks a name up in the catalog's <c>/Names /Dests</c> tree, returning the destination array.</summary>
        public PdfArr? NamedDestination(string name)
        {
            var tree = Catalog.Dict("/Names")?.Dict("/Dests");
            return tree is null ? null : SearchNameTree(tree, name) as PdfArr;
        }

        private PdfValue? SearchNameTree(PdfDict node, string name)
        {
            if (node.Array("/Names") is { } names)
            {
                for (var i = 0; i + 1 < names.Count; i += 2)
                {
                    if (names[i] is PdfText key && key.Value == name)
                        return names[i + 1] is PdfDict wrapper && wrapper.Array("/D") is { } d ? d : names[i + 1];
                }
            }

            if (node.Array("/Kids") is { } kids)
            {
                foreach (var kid in kids.Resolved.OfType<PdfDict>())
                {
                    if (SearchNameTree(kid, name) is { } found)
                        return found;
                }
            }

            return null;
        }

        /// <summary>The interactive form's field dictionaries (<c>/AcroForm /Fields</c>), in order.</summary>
        public IReadOnlyList<PdfDict> Fields =>
            Catalog.Dict("/AcroForm")?.Array("/Fields")?.Resolved.OfType<PdfDict>().ToList() ?? [];

        /// <summary>The interactive form's fields, in order.</summary>
        public IReadOnlyList<FormField> FormFields => Fields.Select(d => new FormField(this, d)).ToList();

        /// <summary>The structure tree root (<c>/StructTreeRoot</c>), or null when the document is untagged.</summary>
        public PdfDict? StructureTreeRoot => Catalog.Dict("/StructTreeRoot");

        /// <summary>Looks <paramref name="key"/> up in the structure tree's <c>/ParentTree</c> number tree.</summary>
        public PdfValue? ParentTreeEntry(int key)
        {
            PdfValue? Search(PdfDict node)
            {
                if (node.Array("/Nums") is { } nums)
                {
                    for (var i = 0; i + 1 < nums.Count; i += 2)
                    {
                        if (nums[i] is PdfNumber n && (int)n.Value == key)
                            return nums[i + 1];
                    }
                }

                foreach (var kid in node.Array("/Kids")?.Resolved.OfType<PdfDict>() ?? [])
                {
                    if (Search(kid) is { } found)
                        return found;
                }

                return null;
            }

            return StructureTreeRoot?.Dict("/ParentTree") is { } tree ? Search(tree) : null;
        }

        /// <summary>The top-level elements of the structure tree, or empty when the document is untagged.</summary>
        public IReadOnlyList<StructElem> StructureRoot =>
            StructureTreeRoot is { } root ? StructureChildren(root).Select(d => new StructElem(this, d)).ToList() : [];

        /// <summary>The structure elements below <paramref name="node"/> (a root or an element), in order.</summary>
        public IReadOnlyList<PdfDict> StructureChildren(PdfDict node)
        {
            var k = node["/K"];
            return k switch
            {
                PdfDict single when single.Name("/Type") != "/MCR" && single.Name("/Type") != "/OBJR" => [single],
                PdfArr array => array.Resolved.OfType<PdfDict>().Where(d => d.Name("/Type") is null or "/StructElem").ToList(),
                _ => [],
            };
        }

        /// <summary>Every kid of a structure element, including marked-content references and object references.</summary>
        public IReadOnlyList<PdfValue> StructureKids(PdfDict node) => node["/K"] switch
        {
            PdfArr array => array.Resolved.ToList(),
            PdfNull or null => [],
            { } single => [single],
        };

        // ---- parsing ----

        private void ReadAll()
        {
            var header = Regex.Match(Encoding.Latin1.GetString(Bytes, 0, Math.Min(32, Bytes.Length)), @"%PDF-(\d\.\d)");
            Version = header.Success ? header.Groups[1].Value : "";

            var text = Encoding.Latin1.GetString(Bytes);
            var objectStart = new Regex(@"(?<![0-9])(\d+)\s+(\d+)\s+obj\b", RegexOptions.Compiled);
            var position = 0;
            while (true)
            {
                var match = objectStart.Match(text, position);
                if (!match.Success)
                    break;

                var parser = new Parser(this, text, match.Index + match.Length);
                var value = parser.ParseValue();

                parser.SkipWhitespace();
                if (value is PdfDict dict && parser.Peek("stream"))
                {
                    parser.Advance(6);
                    if (parser.Current == '\r')
                        parser.Advance(1);
                    if (parser.Current == '\n')
                        parser.Advance(1);

                    var start = parser.Position;
                    var length = dict.Entries.TryGetValue("/Length", out var declared) && declared is PdfNumber n ? (int)n.Value : -1;
                    int end;
                    if (length >= 0 && start + length <= text.Length && text.AsSpan(start + length).TrimStart("\r\n").StartsWith("endstream"))
                    {
                        end = start + length;
                    }
                    else
                    {
                        end = text.IndexOf("endstream", start, StringComparison.Ordinal);
                        if (end < 0)
                            end = text.Length;
                        if (end > start && text[end - 1] == '\n')
                            end--;
                        if (end > start && text[end - 1] == '\r')
                            end--;
                    }

                    value = new PdfStreamObject(this, dict.Entries, Bytes[start..end]);
                    position = Math.Min(text.Length, end);
                }
                else
                {
                    position = parser.Position;
                }

                var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (value is PdfDict objectDict)
                    objectDict.ObjectNumber = number;
                _objects[number] = value;
            }

            // The trailer's dictionary is the last "trailer << ... >>" in the file; a cross-reference stream (not written here) would
            // carry the same keys in its own dictionary.
            var trailerAt = text.LastIndexOf("trailer", StringComparison.Ordinal);
            if (trailerAt >= 0)
            {
                Trailer = (PdfDict)new Parser(this, text, trailerAt + "trailer".Length).ParseValue();
            }
            else
            {
                Trailer = _objects.Values.OfType<PdfStreamObject>().FirstOrDefault(s => s.Name("/Type") == "/XRef")
                    ?? throw new InvalidDataException("no trailer in the PDF");
            }
        }

        private sealed class Parser(PdfFile file, string text, int position)
        {
            public int Position { get; private set; } = position;

            public char Current => Position < text.Length ? text[Position] : '\0';

            public void Advance(int count) => Position += count;

            public bool Peek(string literal) => string.CompareOrdinal(text, Position, literal, 0, literal.Length) == 0;

            public void SkipWhitespace()
            {
                while (Position < text.Length)
                {
                    var c = text[Position];
                    if (c is ' ' or '\t' or '\r' or '\n' or '\f' or '\0')
                    {
                        Position++;
                    }
                    else if (c == '%')
                    {
                        while (Position < text.Length && text[Position] is not ('\r' or '\n'))
                            Position++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private static bool IsDelimiter(char c) => c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

            private static bool IsWhite(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f' or '\0';

            public PdfValue ParseValue()
            {
                SkipWhitespace();
                var c = Current;
                if (c == '<' && Position + 1 < text.Length && text[Position + 1] == '<')
                    return ParseDictionary();
                if (c == '<')
                    return ParseHexString();
                if (c == '[')
                    return ParseArray();
                if (c == '(')
                    return ParseLiteralString();
                if (c == '/')
                    return ParseName();

                var token = ReadToken();
                switch (token)
                {
                    case "true":
                        return new PdfBoolean(true);
                    case "false":
                        return new PdfBoolean(false);
                    case "null":
                        return PdfNull.Instance;
                }

                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new InvalidDataException($"unexpected token '{token}' at {Position}");

                // "N G R" is an indirect reference; anything else is just the number.
                if (token.All(char.IsDigit))
                {
                    var save = Position;
                    SkipWhitespace();
                    var generation = ReadToken();
                    if (generation.Length > 0 && generation.All(char.IsDigit))
                    {
                        SkipWhitespace();
                        if (Current == 'R' && (Position + 1 >= text.Length || IsWhite(text[Position + 1]) || IsDelimiter(text[Position + 1])))
                        {
                            Position++;
                            return new PdfRef((int)number);
                        }
                    }

                    Position = save;
                }

                return new PdfNumber(number);
            }

            private string ReadToken()
            {
                var start = Position;
                while (Position < text.Length && !IsWhite(text[Position]) && !IsDelimiter(text[Position]))
                    Position++;
                return text[start..Position];
            }

            private PdfName ParseName()
            {
                Position++;
                var builder = new StringBuilder("/");
                while (Position < text.Length && !IsWhite(text[Position]) && !IsDelimiter(text[Position]))
                {
                    if (text[Position] == '#' && Position + 2 < text.Length)
                    {
                        builder.Append((char)Convert.ToInt32(text.Substring(Position + 1, 2), 16));
                        Position += 3;
                    }
                    else
                    {
                        builder.Append(text[Position++]);
                    }
                }

                return new PdfName(builder.ToString());
            }

            private PdfDict ParseDictionary()
            {
                Position += 2;
                var entries = new Dictionary<string, PdfValue>(StringComparer.Ordinal);
                while (true)
                {
                    SkipWhitespace();
                    if (Peek(">>"))
                    {
                        Position += 2;
                        return new PdfDict(file, entries);
                    }

                    var key = ParseName();
                    entries[key.Value] = ParseValue();
                }
            }

            private PdfArr ParseArray()
            {
                Position++;
                var items = new List<PdfValue>();
                while (true)
                {
                    SkipWhitespace();
                    if (Current == ']')
                    {
                        Position++;
                        return new PdfArr(file, items);
                    }

                    items.Add(ParseValue());
                }
            }

            private PdfText ParseHexString()
            {
                Position++;
                var digits = new StringBuilder();
                while (Position < text.Length && text[Position] != '>')
                {
                    if (!IsWhite(text[Position]))
                        digits.Append(text[Position]);
                    Position++;
                }

                Position++;
                if (digits.Length % 2 == 1)
                    digits.Append('0');

                var bytes = new byte[digits.Length / 2];
                for (var i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(digits.ToString(i * 2, 2), 16);
                return new PdfText(bytes);
            }

            private PdfText ParseLiteralString()
            {
                Position++;
                var bytes = new List<byte>();
                var depth = 1;
                while (Position < text.Length)
                {
                    var c = text[Position++];
                    if (c == '\\' && Position < text.Length)
                    {
                        var e = text[Position++];
                        switch (e)
                        {
                            case 'n': bytes.Add((byte)'\n'); break;
                            case 'r': bytes.Add((byte)'\r'); break;
                            case 't': bytes.Add((byte)'\t'); break;
                            case 'b': bytes.Add((byte)'\b'); break;
                            case 'f': bytes.Add((byte)'\f'); break;
                            case '\r':
                                if (Current == '\n')
                                    Position++;
                                break;
                            case '\n':
                                break;
                            case >= '0' and <= '7':
                            {
                                var value = e - '0';
                                for (var i = 0; i < 2 && Current is >= '0' and <= '7'; i++)
                                    value = (value * 8) + (text[Position++] - '0');
                                bytes.Add((byte)value);
                                break;
                            }
                            default: bytes.Add((byte)e); break;
                        }
                    }
                    else if (c == '(')
                    {
                        depth++;
                        bytes.Add((byte)c);
                    }
                    else if (c == ')')
                    {
                        if (--depth == 0)
                            break;
                        bytes.Add((byte)c);
                    }
                    else
                    {
                        bytes.Add((byte)c);
                    }
                }

                return new PdfText(bytes.ToArray());
            }
        }
    }

    /// <summary>One element of a tagged PDF's structure tree.</summary>
    internal sealed class StructElem(PdfFile file, PdfDict dict)
    {
        public PdfDict Dict { get; } = dict;

        /// <summary>The structure type, with its leading slash: <c>/P</c>, <c>/Figure</c>.</summary>
        public string? StructureType => Dict.Name("/S");

        public string? AlternateText => Dict.Text("/Alt");

        /// <summary>The element's child structure elements (marked-content and object references are not elements).</summary>
        public IReadOnlyList<StructElem> Kids => file.StructureChildren(Dict).Select(d => new StructElem(file, d)).ToList();

        /// <summary>Every kid, including marked-content ids and object references, resolved.</summary>
        public IReadOnlyList<PdfValue> AllKids => file.StructureKids(Dict);
    }

    internal enum PdfFieldKind { Text, Checkbox, Radio, PushButton, Combo, List, Signature, Unknown }

    /// <summary>A rectangle read from a PDF <c>/Rect</c> (or a box): lower-left and upper-right corners in user space.</summary>
    internal readonly record struct PdfRect(double Left, double Bottom, double Right, double Top)
    {
        public double X => Left;

        public double Y => Bottom;

        public double Width => Right - Left;

        public double Height => Top - Bottom;
    }

    /// <summary>An interactive form field, or one widget kid of a radio group.</summary>
    internal sealed class FormField(PdfFile file, PdfDict dict)
    {
        public PdfDict Dict { get; } = dict;

        private PdfValue? Inherited(string key)
        {
            for (var node = Dict; node is not null; node = node.Dict("/Parent"))
            {
                if (node[key] is { } value)
                    return value;
            }

            return null;
        }

        public string? FieldType => (Inherited("/FT") as PdfName)?.Value;

        public int Flags => (int)((Inherited("/Ff") as PdfNumber)?.Value ?? 0);

        public PdfFieldKind Kind => FieldType switch
        {
            "/Tx" => PdfFieldKind.Text,
            "/Sig" => PdfFieldKind.Signature,
            "/Btn" when (Flags & (1 << 16)) != 0 => PdfFieldKind.PushButton,
            "/Btn" when (Flags & (1 << 15)) != 0 => PdfFieldKind.Radio,
            "/Btn" => PdfFieldKind.Checkbox,
            "/Ch" when (Flags & (1 << 17)) != 0 => PdfFieldKind.Combo,
            "/Ch" => PdfFieldKind.List,
            _ => PdfFieldKind.Unknown,
        };

        public string? PartialFieldName => Dict.Text("/T");

        public int Integer(string key) => (int)(Dict.Number(key) ?? 0);

        /// <summary>The field value: a text string, or a name (with its slash) for a button.</summary>
        public string? Value => Inherited("/V") switch
        {
            PdfName name => name.Value,
            PdfText text => text.Value,
            _ => null,
        };

        public string? AppearanceState => Dict.Name("/AS");

        public string? DefaultAppearance => (Inherited("/DA") as PdfText)?.Value ?? file.Catalog.Dict("/AcroForm")?.Text("/DA");

        public PdfRect Rectangle => Rect(Dict, "/Rect");

        public IReadOnlyList<FormField> Kids =>
            Dict.Array("/Kids")?.Resolved.OfType<PdfDict>().Select(d => new FormField(file, d)).ToList() ?? [];

        /// <summary>The widget annotations that draw this field: its kids, or the field itself when it is its own widget.</summary>
        public IReadOnlyList<FormField> Widgets => Kids is { Count: > 0 } kids ? kids : [this];

        /// <summary>The widget's normal appearance stream (<c>/AP /N</c>); for a two-state button, the one its <c>/AS</c> selects.</summary>
        public PdfStreamObject? NormalAppearance
        {
            get
            {
                var normal = Dict.Dict("/AP")?["/N"];
                return normal switch
                {
                    PdfStreamObject stream => stream,
                    PdfDict states when AppearanceState is { } state => states[state] as PdfStreamObject,
                    _ => null,
                };
            }
        }

        public static PdfRect Rect(PdfDict dict, string key)
        {
            var n = dict.Array(key)!.Resolved.Select(v => ((PdfNumber)v).Value).ToArray();
            return new PdfRect(Math.Min(n[0], n[2]), Math.Min(n[1], n[3]), Math.Max(n[0], n[2]), Math.Max(n[1], n[3]));
        }
    }

    internal static class PdfFieldAssert
    {
        /// <summary>Asserts a field is of <paramref name="kind"/> and hands it back, the way <c>Assert.IsType</c> did for the writer's field classes.</summary>
        public static FormField IsKind(PdfFieldKind kind, FormField field)
        {
            Xunit.Assert.Equal(kind, field.Kind);
            return field;
        }
    }

    /// <summary>One page of a saved document, with the views a test asserts on.</summary>
    internal sealed class PdfPageView(PdfFile file, PdfDict dict)
    {
        public PdfDict Dict { get; } = dict;

        public double Width => PdfFile.Width(Dict);

        public double Height => PdfFile.Height(Dict);

        public IReadOnlyList<PdfDict> Annotations => file.Annotations(Dict);

        public IReadOnlyList<PdfStreamObject> Contents => file.ContentStreams(Dict);

        /// <summary>The page's content stream text, the streams joined in order.</summary>
        public string ContentText => file.ContentText(Dict);

        /// <summary>True when at least one content stream holds data.</summary>
        public bool HasContent => Contents.Any(s => s.StoredBytes.Length > 0);
    }

    internal static class PdfInspectorExtensions
    {
        /// <summary>Saves the document and reads the bytes back, so a test can assert on the structure the writer produced.</summary>
        public static PdfFile Inspect(this PeachPdfDocument document) => PdfFile.Read(document);

        /// <summary>The <c>/Rect</c> of an annotation or widget.</summary>
        public static PdfRect RectOf(this PdfDict dict) => FormField.Rect(dict, "/Rect");

        /// <summary>One page of the saved document.</summary>
        public static PdfPageView Page(this PeachPdfDocument document, int index)
        {
            var file = document.Inspect();
            return new PdfPageView(file, file.Pages[index]);
        }

        /// <summary>The annotation dictionaries on one page of the saved document.</summary>
        public static IReadOnlyList<PdfDict> PageAnnotations(this PeachPdfDocument document, int page = 0)
        {
            var file = document.Inspect();
            return file.Annotations(file.Pages[page]);
        }
    }
}
