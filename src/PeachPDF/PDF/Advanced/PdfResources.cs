// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using System.Collections.Generic;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents a PDF resource object.
/// </summary>
internal sealed class PdfResources : PdfDictionary
{
    // Resource management works roughly like this:
    // When the user creates an TypefaceFont and uses it in the PdfGraphics of a PdfPage, then at the first time
    // a PdfFont is created and cached in the document global font table. If the user creates a new
    // TypefaceFont object for an exisisting PdfFont, the PdfFont object is reused. When the PdfFont is added
    // to the resources of a PdfPage for the first time, it is added to the page local PdfResourceMap for 
    // fonts and automatically associated with a local resource name.

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfResources"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfResources(PdfDocument document)
        : base(document)
    {
        Elements[Keys.ProcSet] = new PdfLiteral("[/PDF/Text/ImageB/ImageC/ImageI]");
    }

    internal PdfResources(PdfDictionary dict)
        : base(dict)
    { }

    /// <summary>
    /// Adds the specified font to this resource dictionary and returns its local resource name.
    /// </summary>
    public string AddFont(PdfFont font)
    {
        string? name;
        if (!_resources.TryGetValue(font, out name))
        {
            name = NextFontName;
            _resources[font] = name;
            if (font.Reference is null)
                Owner._irefTable.Add(font);
            Fonts.Elements[name] = font.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Adds the specified image to this resource dictionary
    /// and returns its local resource name.
    /// </summary>
    public string AddImage(PdfImage image)
    {
        string? name;
        if (!_resources.TryGetValue(image, out name))
        {
            name = NextImageName;
            _resources[image] = name;
            if (image.Reference is null)
                Owner._irefTable.Add(image);
            XObjects.Elements[name] = image.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Adds the specified form object to this resource dictionary
    /// and returns its local resource name.
    /// </summary>
    public string AddForm(PdfFormXObject form)
    {
        string? name;
        if (!_resources.TryGetValue(form, out name))
        {
            name = NextFormName;
            _resources[form] = name;
            if (form.Reference is null)
                Owner._irefTable.Add(form);
            XObjects.Elements[name] = form.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Adds the specified graphics state to this resource dictionary
    /// and returns its local resource name.
    /// </summary>
    public string AddExtGState(PdfExtGState extGState)
    {
        string? name;
        if (!_resources.TryGetValue(extGState, out name))
        {
            name = NextExtGStateName;
            _resources[extGState] = name;
            if (extGState.Reference is null)
                Owner._irefTable.Add(extGState);
            ExtGStates.Elements[name] = extGState.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Adds the specified pattern to this resource dictionary
    /// and returns its local resource name.
    /// </summary>
    public string AddPattern(PdfShadingPattern pattern)
    {
        string? name;
        if (!_resources.TryGetValue(pattern, out name))
        {
            name = NextPatternName;
            _resources[pattern] = name;
            if (pattern.Reference is null)
                Owner._irefTable.Add(pattern);
            Patterns.Elements[name] = pattern.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Adds the specified pattern to this resource dictionary
    /// and returns its local resource name.
    /// </summary>
    public string AddPattern(PdfTilingPattern pattern)
    {
        string? name;
        if (!_resources.TryGetValue(pattern, out name))
        {
            name = NextPatternName;
            _resources[pattern] = name;
            if (pattern.Reference is null)
                Owner._irefTable.Add(pattern);
            Patterns.Elements[name] = pattern.Reference!;
        }
        return name;
    }

    /// <summary>
    /// Gets the fonts map.
    /// </summary>
    internal PdfResourceMap Fonts => _fonts ?? (_fonts = Elements.GetOrCreate<PdfResourceMap>(Keys.Font, ValueCreationFlags.Create));
    PdfResourceMap _fonts = null!;

    /// <summary>
    /// Gets the external objects map.
    /// </summary>
    internal PdfResourceMap XObjects => _xObjects ?? (_xObjects = Elements.GetOrCreate<PdfResourceMap>(Keys.XObject, ValueCreationFlags.Create));
    PdfResourceMap _xObjects = null!;

    // TODO: make own class
    internal PdfResourceMap ExtGStates => _extGStates ?? (_extGStates = Elements.GetOrCreate<PdfResourceMap>(Keys.ExtGState, ValueCreationFlags.Create));
    PdfResourceMap _extGStates = null!;

    // TODO: make own class
    internal PdfResourceMap ColorSpaces => _colorSpaces ?? (_colorSpaces = Elements.GetOrCreate<PdfResourceMap>(Keys.ColorSpace, ValueCreationFlags.Create));
    PdfResourceMap _colorSpaces = null!;

    // TODO: make own class
    internal PdfResourceMap Patterns => _patterns ?? (_patterns = Elements.GetOrCreate<PdfResourceMap>(Keys.Pattern, ValueCreationFlags.Create));
    PdfResourceMap _patterns = null!;

    // TODO: make own class
    internal PdfResourceMap Shadings => _shadings ?? (_shadings = Elements.GetOrCreate<PdfResourceMap>(Keys.Shading, ValueCreationFlags.Create));
    PdfResourceMap _shadings = null!;

    // TODO: make own class
    internal PdfResourceMap Properties => _properties ?? (_properties = Elements.GetOrCreate<PdfResourceMap>(Keys.Properties, ValueCreationFlags.Create));
    PdfResourceMap _properties = null!;

    /// <summary>
    /// Gets a new local name for this resource.
    /// </summary>
    string NextFontName
    {
        get
        {
            string name;
            while (ExistsResourceNames(name = string.Format("/F{0}", _fontNumber++))) { }
            return name;
        }
    }
    int _fontNumber;

    /// <summary>
    /// Gets a new local name for this resource.
    /// </summary>
    string NextImageName
    {
        get
        {
            string name;
            while (ExistsResourceNames(name = string.Format("/I{0}", _imageNumber++))) { }
            return name;
        }
    }
    int _imageNumber;

    /// <summary>
    /// Gets a new local name for this resource.
    /// </summary>
    string NextFormName
    {
        get
        {
            string name;
            while (ExistsResourceNames(name = string.Format("/Fm{0}", _formNumber++))) { }
            return name;
        }
    }
    int _formNumber;

    /// <summary>
    /// Gets a new local name for this resource.
    /// </summary>
    string NextExtGStateName
    {
        get
        {
            string name;
            while (ExistsResourceNames(name = string.Format("/GS{0}", _extGStateNumber++))) { }
            return name;
        }
    }
    int _extGStateNumber;

    /// <summary>
    /// Gets a new local name for this resource.
    /// </summary>
    string NextPatternName
    {
        get
        {
            string name;
            while (ExistsResourceNames(name = string.Format("/Pa{0}", _patternNumber++))) ;
            return name;
        }
    }
    int _patternNumber;

    /// <summary>
    /// Check whether a resource name is already used in the context of this resource dictionary.
    /// PDF4NET uses GUIDs as resource names, but I think this weapon is to heavy.
    /// </summary>
    internal bool ExistsResourceNames(string name)
    {
        // TODO: more precise: is this page imported and is PageOptions != Replace
        // BUG: 
        //if (!Owner.IsImported)
        //  return false;

        // Collect all resouce names of all imported resources.
        if (_importedResourceNames is null)
        {
            _importedResourceNames = new Dictionary<string, object?>();

            if (Elements[Keys.Font] is not null)
                Fonts.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.XObject] is not null)
                XObjects.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.ExtGState] is not null)
                ExtGStates.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.ColorSpace] is not null)
                ColorSpaces.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.Pattern] is not null)
                Patterns.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.Shading] is not null)
                Shadings.CollectResourceNames(_importedResourceNames);

            if (Elements[Keys.Properties] is not null)
                Properties.CollectResourceNames(_importedResourceNames);
        }
        return _importedResourceNames.ContainsKey(name);
        // This is superfluous because PDFsharp resource names cannot be double.
        // importedResourceNames.Add(name, null);
    }

    /// <summary>
    /// All the names of imported resources.
    /// </summary>
    Dictionary<string, object?> _importedResourceNames = null!;

    /// <summary>
    /// Maps all PDFsharp resources to their local resource names.
    /// </summary>
    readonly Dictionary<PdfObject, string> _resources = new Dictionary<PdfObject, string>();

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        /// <summary>
        /// (Optional) A dictionary that maps resource names to graphics state 
        /// parameter dictionaries.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string ExtGState = "/ExtGState";

        /// <summary>
        /// (Optional) A dictionary that maps each resource name to either the name of a
        /// device-dependent color space or an array describing a color space.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string ColorSpace = "/ColorSpace";

        /// <summary>
        /// (Optional) A dictionary that maps each resource name to either the name of a
        /// device-dependent color space or an array describing a color space.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string Pattern = "/Pattern";

        /// <summary>
        /// (Optional; PDF 1.3) A dictionary that maps resource names to shading dictionaries.
        /// </summary>
        [PdfKey("1.3", PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string Shading = "/Shading";

        /// <summary>
        /// (Optional) A dictionary that maps resource names to external objects.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string XObject = "/XObject";

        /// <summary>
        /// (Optional) A dictionary that maps resource names to font dictionaries.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string Font = "/Font";

        /// <summary>
        /// (Optional) An array of predefined procedure set names.
        /// </summary>
        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string ProcSet = "/ProcSet";

        /// <summary>
        /// (Optional; PDF 1.2) A dictionary that maps resource names to property list
        /// dictionaries for marked content.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResourceMap))]
        public const string Properties = "/Properties";

        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        internal static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
