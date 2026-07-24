#nullable disable warnings


using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Internal;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;


namespace PeachPDF.Fonts
{


    internal class FontResolver : IFontResolver
    {
        private static readonly FrozenDictionary<string, string> _systemFontPaths;
        private static readonly FrozenDictionary<string, FontFamilyModel> _systemFamilies;

        private readonly Dictionary<string, byte[]> _CustomFonts = [];
        private readonly Dictionary<string, FontFamilyModel> InstalledFonts;

        /// <summary>
        /// Family names (lowercased, matching <see cref="InstalledFonts"/>'s own key convention)
        /// registered via <see cref="AddFont(Stream, string)"/> on THIS instance - i.e. not just
        /// inherited read-only from the shared static <see cref="_systemFamilies"/> snapshot. Used by
        /// <see cref="XGlyphTypeface.GetOrCreateFrom"/> to decide whether a request for this family must
        /// be routed through this instance's own <see cref="InstanceGlyphTypefacesByKey"/>/
        /// <see cref="InstanceFontResolverInfosByTypefaceKey"/> caches instead of the global,
        /// process-wide ones - two different <see cref="FontResolver"/> instances (i.e. two different
        /// <c>PdfGenerator</c>s) can register DIFFERENT bytes under the SAME custom family name (e.g. two
        /// requests each with their own <c>@font-face</c> for the same CSS family), and must never share
        /// a cache slot for it.
        /// </summary>
        private readonly HashSet<string> _customFamilyNames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Per-face cmap-coverage cache (face name → covered codepoint ranges), populated lazily the
        /// first time a face with no explicit <c>unicode-range</c> is coverage-tested during
        /// per-codepoint matching. Instance-scoped (like <see cref="_CustomFonts"/>) so it is collected
        /// with this resolver and never shared across <c>PdfGenerator</c> instances.
        /// </summary>
        private readonly Dictionary<string, IReadOnlyList<RuneRange>> _coverageCache = new(StringComparer.Ordinal);

        /// <summary>
        /// This instance's own typeface-key-keyed glyph-typeface cache, used only for custom
        /// (<see cref="_customFamilyNames"/>) families - see <see cref="XGlyphTypeface.GetOrCreateFrom"/>.
        /// A plain instance field, not a static/global root, so it's garbage-collected along with this
        /// <see cref="FontResolver"/> (and the <c>PdfSharpAdapter</c>/<c>PdfGenerator</c> that owns it) -
        /// it cannot outlive them, and needs no explicit disposal.
        /// </summary>
        internal Dictionary<string, XGlyphTypeface> InstanceGlyphTypefacesByKey { get; } = new();

        /// <summary>
        /// This instance's own typeface-key-keyed resolver-info cache - the per-instance counterpart to
        /// <see cref="FontFactory"/>'s global <c>FontResolverInfosByName</c>, used only for custom
        /// families. See <see cref="InstanceGlyphTypefacesByKey"/>.
        /// </summary>
        internal Dictionary<string, FontResolverInfo> InstanceFontResolverInfosByTypefaceKey { get; } = new();

        /// <summary>
        /// This instance's own typeface-key-keyed <see cref="FontDescriptor"/> cache - the per-instance
        /// counterpart to the global, static <c>FontDescriptorCache</c> (which is ALSO keyed purely by
        /// the typeface key string, with no notion of which resolver instance produced the underlying
        /// glyph data - see <see cref="XGlyphTypeface.OwningInstanceResolver"/>). Used only for custom
        /// families; without this, a descriptor built from one instance's custom font bytes would leak
        /// into another instance's request for the same family+style, exactly like the collision the
        /// glyph-typeface/resolver-info split above fixes, just one layer further down (font metrics/
        /// embedding data instead of glyph outlines).
        /// </summary>
        internal Dictionary<string, FontDescriptor> InstanceFontDescriptorsByKey { get; } = new();

        /// <summary>
        /// Whether <paramref name="familyName"/> was registered via <see cref="AddFont(Stream, string)"/>
        /// on this specific instance (as opposed to being resolvable purely from the shared, immutable,
        /// safe-to-share-globally system-font snapshot).
        /// </summary>
        internal bool IsCustomFamily(string familyName) => _customFamilyNames.Contains(familyName);

        public static string[] SupportedFonts { get; }

        private static readonly string[] FontExtensions = ["*.ttf", "*.otf"];

        private static string[] GetFontFiles(string dir)
        {
            if (!Directory.Exists(dir))
                return [];

            try
            {
                return FontExtensions
                    .SelectMany(pattern => Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                // Some directories (e.g. Android's /data/fonts, /product/fonts) may exist
                // but be unreadable depending on OEM/OS-version SELinux policy. Treat that
                // the same as "no fonts here" rather than failing font discovery entirely.
                return [];
            }
        }

        static FontResolver()
        {
            var isAndroid = System.OperatingSystem.IsAndroid();
            var isIOS = System.OperatingSystem.IsIOS();
            var isOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            var isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            SupportedFonts = DiscoverSupportedFonts(isOSX, isLinux, isWindows, isAndroid, isIOS);

            (_systemFontPaths, _systemFamilies) = ParseSystemFonts(SupportedFonts);
        }

        internal static string[] DiscoverSupportedFonts(bool isOSX, bool isLinux, bool isWindows, bool isAndroid, bool isIOS)
        {
            // Checked first: RuntimeInformation.IsOSPlatform(OSPlatform.Linux) is not
            // guaranteed to exclude Android (Linux-kernel-based), so Android/iOS must be
            // routed to their own branches before isLinux/isOSX are consulted.
            if (isAndroid)
            {
                var candidateDirs = new[] { "/system/fonts", "/product/fonts", "/data/fonts" };

                return candidateDirs
                    .SelectMany(GetFontFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (isIOS)
            {
                // iOS sandboxes apps away from system font files entirely, and CoreText
                // exposes fonts only as opaque handles (CTFont/UIFont) with no public API
                // to extract raw file bytes. There is nothing on-disk this resolver can
                // discover here. iOS apps should embed their own fonts and register them
                // via PdfGenerator.AddFontFromStream.
                return [];
            }

            if (isOSX)
            {
                var homeDir = System.Environment.GetEnvironmentVariable("HOME");
                var candidateDirs = new List<string> { "/System/Library/Fonts", "/Library/Fonts" };
                if (!string.IsNullOrEmpty(homeDir))
                    candidateDirs.Add(Path.Combine(homeDir, "Library", "Fonts"));

                return candidateDirs
                    .SelectMany(GetFontFiles)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (isLinux)
            {
                return LinuxSystemFontResolver.Resolve();
            }

            if (isWindows)
            {
                var fontDir = System.Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Fonts");
                var fontPaths = new List<string>(GetFontFiles(fontDir));

                var appdataFontDir = System.Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Windows\Fonts");
                fontPaths.AddRange(GetFontFiles(appdataFontDir));

                return fontPaths.ToArray();
            }

            // Other platforms without system font discovery (tvOS, watchOS, browser/WASM,
            // ...): start with no system fonts and rely on fonts registered via
            // PdfGenerator.AddFontFromStream.
            return [];
        }


        public FontResolver()
        {
            InstalledFonts = new Dictionary<string, FontFamilyModel>(_systemFamilies);
        }

        private readonly struct FontFileInfo
        {
            private FontFileInfo(TtfFontDescription fontDescription)
            {
                this.FontDescription = fontDescription;
            }

            public TtfFontDescription FontDescription { get; }

            public string FamilyName => this.FontDescription.FontFamilyInvariantCulture;

            public XFontStyle GuessFontStyle() => this.FontDescription.Style;

            public static FontFileInfo Load(string path)
            {
                var fontDescription = TtfFontDescription.LoadDescription(path);
                return new FontFileInfo(fontDescription);
            }

            public static FontFileInfo Load(Stream stream)
            {
                var fontDescription = TtfFontDescription.LoadDescription(stream);
                return new FontFileInfo(fontDescription);
            }
        }

        public void AddFont(Stream stream, string fontFamilyName)
        {
            AddFont(stream, fontFamilyName, weightOverride: null, isItalicOverride: null, stretchOverride: null);
        }

        /// <summary>
        /// Registers a font under <paramref name="fontFamilyName"/>, optionally overriding the face's own
        /// sniffed weight/style with the values an <c>@font-face</c> rule declared for it (its
        /// <c>font-weight</c>/<c>font-style</c> descriptors are authoritative for how that specific
        /// resource participates in matching, independent of what the file's own internal tables say -
        /// see <c>DomParser.CascadeApplyStyleFonts</c>). Null means "use the value sniffed from the file
        /// itself" (the previous, only, behavior).
        /// <para><paramref name="unicodeRanges"/> restricts which codepoints this face is used for (an
        /// <c>@font-face</c> <c>unicode-range</c> descriptor, or an explicit registration list); null
        /// means "no restriction - use this face for whatever its font's cmap actually covers".</para>
        /// </summary>
        public void AddFont(Stream stream, string fontFamilyName, int? weightOverride, bool? isItalicOverride, int? stretchOverride = null, IReadOnlyList<RuneRange>? unicodeRanges = null)
        {
            var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);

            var fontBytes = memoryStream.ToArray();
            memoryStream.Seek(0, SeekOrigin.Begin);

            var fontFileInfo = FontFileInfo.Load(memoryStream);
            var key = fontFamilyName.ToLower();
            _customFamilyNames.Add(key);

            var weight = weightOverride ?? fontFileInfo.FontDescription.Weight;
            var isItalic = isItalicOverride ?? fontFileInfo.FontDescription.Style is XFontStyle.Italic or XFontStyle.BoldItalic;
            var stretch = stretchOverride ?? fontFileInfo.FontDescription.Stretch;

            // The face name is the identity under which the bytes are stored and later fetched
            // (GetFont) for embedding. It is normally the font's own internal name, which keeps every
            // existing test asserting FaceName == the file's internal name valid. But two DIFFERENT fonts
            // can share one internal name (a common webfont-subset pattern - e.g. every "Roboto" subset
            // file reports "Roboto"); those must not collide in _CustomFonts (the second would overwrite
            // the first's bytes). So when a *different* byte set is already registered under this internal
            // name, disambiguate with a content checksum. Browsers identify a font resource by its bytes,
            // never by the file's self-reported name - this makes the byte store do the same.
            var internalName = fontFileInfo.FontDescription.FontNameInvariantCulture;
            var faceName = internalName;
            if (_CustomFonts.TryGetValue(internalName, out var existingBytes) && !existingBytes.AsSpan().SequenceEqual(fontBytes))
            {
                faceName = $"{internalName}#{FontHelper.CalcChecksum(fontBytes):x}";
            }

            // The STORED description's own Weight/Style/Stretch must reflect the override too, not just
            // the matching axes it's filed under - FontResolver.ResolveTypeface's synthesis decision
            // (and any other future consumer) reads these fields directly off the returned face. The face
            // name is likewise stored on it, so ResolveTypeface hands back the (possibly disambiguated)
            // name GetFont expects.
            var effectiveStyle = (isItalic, weight >= 700) switch
            {
                (true, true) => XFontStyle.BoldItalic,
                (true, false) => XFontStyle.Italic,
                (false, true) => XFontStyle.Bold,
                (false, false) => XFontStyle.Regular
            };
            var baseDescription = weightOverride is null && isItalicOverride is null && stretchOverride is null
                ? fontFileInfo.FontDescription
                : fontFileInfo.FontDescription with { Weight = weight, Style = effectiveStyle, Stretch = stretch };
            var faceDescription = baseDescription with { FontNameInvariantCulture = faceName };

            var entry = new FontFaceEntry(weight, isItalic, stretch, unicodeRanges, faceDescription);

            // family may be a shared static FontFamilyModel from _systemFamilies (or an already-private
            // clone from a prior AddFont call on this instance). Clone before mutating so we never write
            // into state shared with other FontResolver instances/threads. Replace any existing face
            // occupying the same slot (same axes AND same range set) - a re-registration - while letting a
            // same-axes face with a *different* range set coexist (that is exactly the unicode-range
            // subset case).
            var clonedFamily = new FontFamilyModel { Name = InstalledFonts.TryGetValue(key, out var family) ? family.Name : key };
            if (family is not null)
            {
                foreach (var face in family.Faces)
                {
                    if (!IsSameFaceSlot(face, weight, isItalic, stretch, unicodeRanges))
                        clonedFamily.Faces.Add(face);
                }
            }
            clonedFamily.Faces.Add(entry);
            InstalledFonts[key] = clonedFamily;

            _CustomFonts[faceName] = fontBytes;
        }

        private static bool IsSameFaceSlot(FontFaceEntry entry, int weight, bool isItalic, int stretch, IReadOnlyList<RuneRange>? ranges)
        {
            return entry.Weight == weight && entry.Italic == isItalic && entry.Stretch == stretch
                   && RangesEqual(entry.ExplicitRanges, ranges);
        }

        private static bool RangesEqual(IReadOnlyList<RuneRange>? a, IReadOnlyList<RuneRange>? b)
        {
            if (a is null || b is null)
                return a is null && b is null;
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }
            return true;
        }

        private static (FrozenDictionary<string, string> Paths, FrozenDictionary<string, FontFamilyModel> Families) ParseSystemFonts(string[] sSupportedFonts)
        {
            var fontPaths = new Dictionary<string, string>();
            var tempFontInfoList = new List<FontFileInfo>();

            foreach (var fontPathFile in sSupportedFonts)
            {
                try
                {
                    var fontInfo = FontFileInfo.Load(fontPathFile);
                    Debug.WriteLine(fontPathFile);
                    tempFontInfoList.Add(fontInfo);

                    if (!fontPaths.ContainsKey(fontInfo.FontDescription.FontNameInvariantCulture))
                    {
                        fontPaths.Add(fontInfo.FontDescription.FontNameInvariantCulture, fontPathFile);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.WriteLine(e);
                }
            }

            var families = new Dictionary<string, FontFamilyModel>();

            // Deserialize all font families
            foreach (var familyGroup in tempFontInfoList.GroupBy(info => info.FamilyName))
                try
                {
                    var familyName = familyGroup.Key;
                    var family = DeserializeFontFamily(familyName, familyGroup);
                    families.Add(familyName.ToLower(), family);
                }
                catch (System.Exception e)
                {
                    Debug.WriteLine(e);
                }

            return (fontPaths.ToFrozenDictionary(), families.ToFrozenDictionary());
        }


        private static FontFamilyModel DeserializeFontFamily(string fontFamilyName, IEnumerable<FontFileInfo> fontList)
        {
            var font = new FontFamilyModel { Name = fontFamilyName };

            foreach (var info in fontList)
            {
                var isItalic = info.FontDescription.Style is XFontStyle.Italic or XFontStyle.BoldItalic;
                // System fonts declare no explicit unicode-range; their effective coverage is whatever
                // their cmap supports (resolved lazily). Keep the first face seen per (weight, italic,
                // stretch) - the same de-dup the previous dictionary key provided.
                if (!font.Faces.Any(f => f.Weight == info.FontDescription.Weight && f.Italic == isItalic && f.Stretch == info.FontDescription.Stretch))
                    font.Faces.Add(new FontFaceEntry(info.FontDescription.Weight, isItalic, info.FontDescription.Stretch, null, info.FontDescription));
            }

            return font;
        }

        public virtual byte[] GetFont(string fontFaceName)
        {
            if (_CustomFonts.TryGetValue(fontFaceName, out var fontBytes))
            {
                return fontBytes;
            }

            if (_systemFontPaths.TryGetValue(fontFaceName, out var fontPath))
            {
                return File.ReadAllBytes(fontPath);
            }

            throw new ArgumentOutOfRangeException(nameof(fontFaceName), "Unknown Font Face Name");
        }

        public bool HasFont(string fontFaceName)
        {
            return _CustomFonts.ContainsKey(fontFaceName) || _systemFontPaths.ContainsKey(fontFaceName);
        }

        /// <summary>
        /// Whether any face of <paramref name="familyName"/> declares an explicit <c>unicode-range</c>.
        /// The layout fast path uses this to decide whether a word must be resolved per-codepoint (to
        /// honor a ranged face) even when the family's default face already covers every character.
        /// </summary>
        public bool HasExplicitRanges(string familyName)
        {
            return InstalledFonts.TryGetValue(familyName.ToLower(), out var family)
                   && family.Faces.Any(f => f.ExplicitRanges is not null);
        }

        public bool NullIfFontNotFound { get; set; } = false;

        public virtual FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            ResolveTypeface(familyName, isBold ? 700 : 400, isItalic);

        public virtual FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic) =>
            ResolveTypeface(familyName, weight, isItalic, TtfFontDescription.DefaultStretch);

        public virtual FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic, int stretch) =>
            ResolveTypeface(familyName, weight, isItalic, stretch, codepoint: null);

        /// <summary>
        /// Resolves a face for <paramref name="familyName"/> at the requested axes, optionally restricted
        /// to faces that actually cover <paramref name="codepoint"/> (its <c>unicode-range</c> or, absent
        /// that, its cmap coverage). A codepoint-scoped request that finds no covering face returns null,
        /// so per-codepoint font matching can move on to the next family in the stack instead of
        /// substituting a face that cannot render the character. A codepoint-less request keeps the
        /// previous behavior exactly (no coverage filter, plus the "give the caller something" last
        /// resort), so ordinary box/metrics resolution is unchanged.
        /// </summary>
        public virtual FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic, int stretch, Rune? codepoint)
        {
            if (InstalledFonts.Count == 0)
                throw new System.IO.FileNotFoundException("No Fonts installed on this device!");

            if (InstalledFonts.TryGetValue(familyName.ToLower(), out var family))
            {
                if (TryFindNearestFace(family, weight, isItalic, stretch, codepoint, out var face))
                {
                    // The chosen face may be a compromise (nearest-weight/slant match, not exact) -
                    // decide whether the gap is large enough that faux-bold/italic synthesis should
                    // kick in, so e.g. "font-weight: bold" against a Regular-only family doesn't render
                    // with zero visual distinction. Threshold mirrors the common UA convention that
                    // weights >=600 read as "bold" and <600 don't; a request in the bold range that
                    // only found a lighter-than-600 face needs synthesis, but a request that found ANY
                    // face already at/above 600 (e.g. asked for 600, only 700 registered) does not.
                    var resolvedIsItalic = face.Style is XFontStyle.Italic or XFontStyle.BoldItalic;
                    var mustSimulateBold = weight >= 600 && face.Weight < 600;
                    var mustSimulateItalic = isItalic && !resolvedIsItalic;

                    return new FontResolverInfo(face.FontNameInvariantCulture, mustSimulateBold, mustSimulateItalic);
                }

                // Family is registered but has no face covering the request. For a codepoint-scoped
                // request that is a real "this family can't render this character" signal (fall through
                // to null below); for a codepoint-less request it should not happen (every family has at
                // least one face) and falls through to the same last resort as an unknown family.
            }

            // A codepoint-scoped miss must not substitute an arbitrary non-covering face - report it so
            // the caller tries the next family (and ultimately the box default).
            if (codepoint is not null)
                return null;

            if (NullIfFontNotFound)
                return null;

            var description = InstalledFonts.First().Value.Faces[0].Description;
            return new FontResolverInfo(description.FontNameInvariantCulture);
        }

        /// <summary>
        /// CSS Fonts Level 4 §5 face matching. When <paramref name="codepoint"/> is supplied, only faces
        /// whose effective coverage (explicit <c>unicode-range</c>, else lazily-computed cmap coverage)
        /// includes it are candidates; among equally-good matches the last-declared wins (CSS cascade
        /// order for overlapping ranges). Otherwise every face is a candidate. Within the candidates it
        /// narrows italic/slant first, then stretch, then weight; an exact axis match short-circuits.
        /// Returns false when no candidate face qualifies.
        /// </summary>
        private bool TryFindNearestFace(FontFamilyModel family, int weight, bool isItalic, int stretch, Rune? codepoint, out TtfFontDescription face)
        {
            face = default;

            List<FontFaceEntry> covering = codepoint is Rune rune
                ? family.Faces.Where(f => FaceCovers(f, rune)).ToList()
                : family.Faces;

            if (covering.Count == 0)
                return false;

            var exact = covering.Where(f => f.Weight == weight && f.Italic == isItalic && f.Stretch == stretch).ToList();
            if (exact.Count > 0)
            {
                face = exact[^1].Description;
                return true;
            }

            var sameSlant = covering.Where(f => f.Italic == isItalic).ToList();
            var candidates = sameSlant.Count > 0 ? sameSlant : covering;

            var availableStretches = candidates.Select(f => f.Stretch).Distinct().ToList();
            var chosenStretch = PickNearestStretch(availableStretches, stretch);
            var stretchCandidates = candidates.Where(f => f.Stretch == chosenStretch).ToList();

            var availableWeights = stretchCandidates.Select(f => f.Weight).Distinct().ToList();
            var chosenWeight = PickNearestWeight(availableWeights, weight);

            face = stretchCandidates.Last(f => f.Weight == chosenWeight).Description;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="entry"/> is used for <paramref name="rune"/>: inside its declared
        /// <c>unicode-range</c> if it has one, else inside what its font's cmap actually covers (computed
        /// lazily and cached per face, so only faces of an actually-requested family are ever scanned).
        /// </summary>
        private bool FaceCovers(FontFaceEntry entry, Rune rune)
        {
            var ranges = entry.ExplicitRanges ?? GetOrComputeCoverage(entry.Description.FontNameInvariantCulture);
            return CMapCoverage.Contains(ranges, rune);
        }

        private IReadOnlyList<RuneRange> GetOrComputeCoverage(string faceName)
        {
            if (_coverageCache.TryGetValue(faceName, out var cached))
                return cached;

            IReadOnlyList<RuneRange> coverage;
            try
            {
                var fontSource = XFontSource.GetOrCreateFrom(GetFont(faceName));
                coverage = CMapCoverage.Extract(fontSource.Fontface?.cmap);
            }
            catch
            {
                coverage = [];
            }

            _coverageCache[faceName] = coverage;
            return coverage;
        }

        /// <summary>
        /// CSS Fonts Level 4 §5.2's nearest-stretch search order: a target at or narrower than normal (5)
        /// searches narrower first (down to 1), then wider; a target wider than normal searches wider
        /// first (up to 9), then narrower. <paramref name="availableStretches"/> must be non-empty.
        /// </summary>
        private static int PickNearestStretch(List<int> availableStretches, int target)
        {
            if (availableStretches.Contains(target))
                return target;

            IEnumerable<int> Search()
            {
                if (target <= TtfFontDescription.DefaultStretch)
                {
                    return availableStretches.Where(s => s < target).OrderByDescending(s => s)
                        .Concat(availableStretches.Where(s => s > target).OrderBy(s => s));
                }

                return availableStretches.Where(s => s > target).OrderBy(s => s)
                    .Concat(availableStretches.Where(s => s < target).OrderByDescending(s => s));
            }

            return Search().First();
        }

        /// <summary>
        /// CSS Fonts Level 4 §5.2's nearest-weight search order (the standard browser algorithm, not a
        /// novel design here): a target in [400,500] searches upward to 500 first, then below the
        /// target, then above 500; a target below 400 searches downward first, then upward; a target
        /// above 500 searches upward first, then downward. <paramref name="availableWeights"/> must be
        /// non-empty and is assumed to NOT already contain an exact match for <paramref name="target"/>
        /// (the caller checks that separately, since an exact match also has to match the requested
        /// italic-ness, which this purely-numeric helper doesn't know about).
        /// </summary>
        private static int PickNearestWeight(List<int> availableWeights, int target)
        {
            IEnumerable<int> Search()
            {
                if (target is >= 400 and <= 500)
                {
                    return availableWeights.Where(w => w >= target && w <= 500).OrderBy(w => w)
                        .Concat(availableWeights.Where(w => w < target).OrderByDescending(w => w))
                        .Concat(availableWeights.Where(w => w > 500).OrderBy(w => w));
                }

                if (target < 400)
                {
                    return availableWeights.Where(w => w < target).OrderByDescending(w => w)
                        .Concat(availableWeights.Where(w => w > target).OrderBy(w => w));
                }

                return availableWeights.Where(w => w > target).OrderBy(w => w)
                    .Concat(availableWeights.Where(w => w < target).OrderByDescending(w => w));
            }

            return Search().First();
        }
    }
}
