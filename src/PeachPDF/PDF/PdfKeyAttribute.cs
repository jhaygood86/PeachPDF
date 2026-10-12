// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Diagnostics.CodeAnalysis;

namespace PeachPDF.PDF;

/// <summary>
/// Specifies the type of a key's value in a dictionary.
/// </summary>
[Flags]
internal enum PdfKeyType
{
    Name = 0x00000001,
    String = 0x00000002,
    Boolean = 0x00000003,
    Integer = 0x00000004,
    Real = 0x00000005,
    Date = 0x00000006,
    Rectangle = 0x00000007,
    Array = 0x00000008,
    Dictionary = 0x00000009,
    Stream = 0x0000000A,
    NumberTree = 0x0000000B,
    Function = 0x0000000C,
    TextString = 0x0000000D,
    ByteString = 0x0000000E,
    NameTree = 0x0000000F,

    NameOrArray = 0x00000010,
    NameOrDictionary = 0x00000020,
    ArrayOrDictionary = 0x00000030,
    StreamOrArray = 0x00000040,
    StreamOrName = 0x00000050,
    ArrayOrNameOrString = 0x00000060,
    FunctionOrName = 0x000000070,
    Various = 0x000000080,

    TypeMask = 0x000000FF,

    Optional = 0x00000100,
    Required = 0x00000200,
    Inheritable = 0x00000400,
    MustBeIndirect = 0x00001000,
    MustNotBeIndirect = 0x00002000,
}

/// <summary>
/// Summary description for PdfKey.
/// </summary>
internal class PdfKeyAttribute : Attribute
{
    public PdfKeyAttribute()
    { }

    public PdfKeyAttribute(PdfKeyType keyType)
    {
        //_version = version;
        PdfKeyType = keyType;
    }

    public PdfKeyAttribute(string version, PdfKeyType keyType)
    {
        _version = version;
        PdfKeyType = keyType;
    }

    public PdfKeyAttribute(PdfKeyType keyType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
     Type objectType)
    {
        //_version = version;
        PdfKeyType = keyType;
        _objectType = objectType;
    }

    public PdfKeyAttribute(string version, PdfKeyType keyType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    Type objectType)
    {
        //_version = version;
        PdfKeyType = keyType;
        _objectType = objectType;
    }

    public string Version
    {
        get => _version;
        set => _version = value;
    }
    string _version = "1.0";

    public PdfKeyType PdfKeyType
    {
        get => _entryType;
        set => _entryType = value;
    }
    PdfKeyType _entryType;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    public Type ObjectType
    {
        get => _objectType;
        set => _objectType = value;
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
    Type _objectType = null!;

    public string FixedValue
    {
        get => _fixedValue;
        set => _fixedValue = value;
    }
    string _fixedValue = null!;
}
