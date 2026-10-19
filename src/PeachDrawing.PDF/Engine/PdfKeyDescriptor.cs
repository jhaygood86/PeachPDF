// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PeachDrawing.PDF.Engine;

/// <summary>
/// Holds information about the value of a key in a dictionary. This information is used to create
/// and interpret this value.
/// </summary>
internal sealed class PdfKeyDescriptor
{
    /// <summary>
    /// Initializes a new instance of PdfKeyDescriptor from the specified attribute during a KeysMeta
    /// initializes itself using reflection.
    /// </summary>
    public PdfKeyDescriptor(PdfKeyAttribute attribute)
    {
        _version = attribute.Version;
        _keyType = attribute.PdfKeyType;
        _fixedValue = attribute.FixedValue;
        _objectType = attribute.ObjectType;

        if (_version == "")
            _version = "1.0";
    }

    /// <summary>
    /// Gets or sets the PDF version starting with the availability of the described key.
    /// </summary>
    public string Version
    {
        get => _version;
        set => _version = value;
    }
    string _version;

    public PdfKeyType PdfKeyType
    {
        get => _keyType;
        set => _keyType = value;
    }
    PdfKeyType _keyType;

    public string KeyValue
    {
        get => _keyValue;
        set => _keyValue = value;
    }
    string _keyValue = null!;

    public string FixedValue => _fixedValue;
    readonly string _fixedValue;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    public Type ObjectType
    {
        get => _objectType;
        set => _objectType = value;
    }
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    Type _objectType;

    // These types are not yet used/implemented.
    static readonly FrozenSet<PdfKeyType> NotYetImplementedKeyTypes = new HashSet<PdfKeyType>
    {
        PdfKeyType.NumberTree,
        PdfKeyType.NameOrArray,
        PdfKeyType.ArrayOrDictionary,
        PdfKeyType.StreamOrArray,
    }.ToFrozenSet();

    /// <summary>
    /// Returns the type of the object to be created as value for the described key.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    public Type? GetValueType()
    {
        if (_objectType is not null)
            return _objectType;

        // If we have no ObjectType specified, use the PdfKeyType enumeration.
        PdfKeyType keyType = _keyType & PdfKeyType.TypeMask;

        // Each arm below returns typeof(...) directly (rather than through a lookup table) so the
        // trim/AOT analyzer can keep tracking DynamicallyAccessedMembers through to the constructors
        // PdfDictionary.CreateDictionary/CreateArray reflect on - a FrozenDictionary<PdfKeyType, Type>
        // lookup severs that flow (IL2068) since TryGetValue's out Type carries no annotation.
        Type? type = keyType switch
        {
            PdfKeyType.Name => typeof(PdfName),
            PdfKeyType.String => typeof(PdfString),
            PdfKeyType.Boolean => typeof(PdfBoolean),
            PdfKeyType.Integer => typeof(PdfInteger),
            PdfKeyType.Real => typeof(PdfReal),
            PdfKeyType.Date => typeof(PdfDate),
            PdfKeyType.Rectangle => typeof(PdfRectangle),
            PdfKeyType.Array => typeof(PdfArray),
            PdfKeyType.Dictionary or PdfKeyType.Stream => typeof(PdfDictionary),
            _ => null
        };
        if (type is not null)
            return type;

        if (NotYetImplementedKeyTypes.Contains(keyType))
            throw new NotImplementedException($"PdfKeyType.{keyType}");

        if (keyType == PdfKeyType.ArrayOrNameOrString)
            return null; // HACK: Make PdfOutline work

        return ReportInvalidKeyType();
    }

    // No caller passes a PdfKeyType this doesn't recognize - excluded because a test driving it would
    // have to call Debug.Assert(false), which xUnit's default trace listener turns into a test
    // failure rather than letting it be asserted against.
    [ExcludeFromCodeCoverage]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    private Type? ReportInvalidKeyType()
    {
        Debug.Assert(false, "Invalid PdfKeyType: " + _keyType);
        return null;
    }
}
