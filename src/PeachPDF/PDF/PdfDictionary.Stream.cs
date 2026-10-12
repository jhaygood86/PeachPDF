// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachPDF.PDF.Drawing;
using PeachPDF.PDF.Advanced;
using PeachPDF.PDF.Filters;
using PeachPDF.PDF.Internal;
using PeachPDF.PDF.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PeachPDF.PDF;

internal partial class PdfDictionary
{
    /// <summary>
    /// The PDF stream objects.
    /// </summary>
    internal sealed class PdfStream
    {
        internal PdfStream(PdfDictionary ownerDictionary)
        {
            ArgumentNullException.ThrowIfNull(ownerDictionary);
            _ownerDictionary = ownerDictionary;
        }

        /// <summary>
        /// A .NET string can contain char(0) as a valid character.
        /// </summary>
        internal PdfStream(byte[] value, PdfDictionary owner)
            : this(owner)
        {
            _value = value;
        }

/// <summary>
        /// Moves this instance to another dictionary during object type transformation.
        /// </summary>
        internal void ChangeOwner(PdfDictionary dict)
        {
            if (_ownerDictionary is not null)
            {
                // ???
            }

            // Set new owner.
            _ownerDictionary = dict;

            // Set owners stream to this.
            _ownerDictionary._stream = this;
        }

        /// <summary>
        /// The dictionary the stream belongs to.
        /// </summary>
        PdfDictionary _ownerDictionary;

        /// <summary>
        /// Gets the length of the stream, i.e. the actual number of bytes in the stream.
        /// </summary>
        public int Length => _value is not null ? _value.Length : 0;

        /// <summary>
        /// Get or sets the bytes of the stream as they are, i.e. if one or more filters exist the bytes are
        /// not unfiltered.
        /// </summary>
        public byte[] Value
        {
            get => _value;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _value = value;
                _ownerDictionary.Elements.SetInteger(Keys.Length, value.Length);
            }
        }
        byte[] _value = null!;

        /// <summary>
        /// Compresses the stream with the FlateDecode filter.
        /// If a filter is already defined, the function has no effect.
        /// </summary>
        public void Zip()
        {
            if (_value is null)
                return;

            if (!_ownerDictionary.Elements.ContainsKey(Keys.Filter))
            {
                _value = StreamCompression.Encode(_ownerDictionary._document.Options, _value, out var filterName);
                _ownerDictionary.Elements[Keys.Filter] = new PdfName(filterName);
                _ownerDictionary.Elements[Keys.Length] = new PdfInteger(_value.Length);
            }
        }

        /// <summary>
        /// Returns the stream content as a raw string.
        /// </summary>
        public override string ToString()
        {
            if (_value is null)
                return "«null»";

            return PdfEncoders.LatinOneByteEncoding.GetString(_value, 0, _value.Length);
        }

        ///// <summary>
        ///// Converts a raw encoded string into a byte array.
        ///// </summary>

        /// <summary>
        /// Common keys for all streams.
        /// </summary>
        internal class Keys : KeysBase
        {
            // ReSharper disable InconsistentNaming

            /// <summary>
            /// (Required) The number of bytes from the beginning of the line following the keyword
            /// stream to the last byte just before the keyword endstream. (There may be an additional
            /// EOL marker, preceding endstream, that is not included in the count and is not logically
            /// part of the stream data.)
            /// </summary>
            [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
            public const string Length = "/Length";

            /// <summary>
            /// (Optional) The name of a filter to be applied in processing the stream data found between
            /// the keywords stream and endstream, or an array of such names. Multiple filters should be
            /// specified in the order in which they are to be applied.
            /// </summary>
            [PdfKey(PdfKeyType.NameOrArray | PdfKeyType.Optional)]
            public const string Filter = "/Filter";

            /// <summary>
            /// (Optional) A parameter dictionary or an array of such dictionaries, used by the filters
            /// specified by Filter. If there is only one filter and that filter has parameters, DecodeParms
            /// must be set to the filter’s parameter dictionary unless all the filter’s parameters have
            /// their default values, in which case the DecodeParms entry may be omitted. If there are 
            /// multiple filters and any of the filters has parameters set to nondefault values, DecodeParms
            /// must be an array with one entry for each filter: either the parameter dictionary for that
            /// filter, or the null object if that filter has no parameters (or if all of its parameters have
            /// their default values). If none of the filters have parameters, or if all their parameters
            /// have default values, the DecodeParms entry may be omitted.
            /// </summary>
            [PdfKey(PdfKeyType.ArrayOrDictionary | PdfKeyType.Optional)]
            public const string DecodeParms = "/DecodeParms";

            /// <summary>
            /// (Optional; PDF 1.2) The file containing the stream data. If this entry is present, the bytes
            /// between stream and endstream are ignored, the filters are specified by FFilter rather than
            /// Filter, and the filter parameters are specified by FDecodeParms rather than DecodeParms.
            /// However, the Length entry should still specify the number of those bytes. (Usually, there are
            /// no bytes and Length is 0.)
            /// </summary>
            [PdfKey("1.2", PdfKeyType.String | PdfKeyType.Optional)]
            public const string F = "/F";

            /// <summary>
            /// (Optional; PDF 1.2) The name of a filter to be applied in processing the data found in the
            /// stream’s external file, or an array of such names. The same rules apply as for Filter.
            /// </summary>
            [PdfKey("1.2", PdfKeyType.NameOrArray | PdfKeyType.Optional)]
            public const string FFilter = "/FFilter";

            /// <summary>
            /// (Optional; PDF 1.2) A parameter dictionary, or an array of such dictionaries, used by the
            /// filters specified by FFilter. The same rules apply as for DecodeParms.
            /// </summary>
            [PdfKey("1.2", PdfKeyType.ArrayOrDictionary | PdfKeyType.Optional)]
            public const string FDecodeParms = "/FDecodeParms";

            /// <summary>
            /// Optional; PDF 1.5) A non-negative integer representing the number of bytes in the decoded
            /// (defiltered) stream. It can be used to determine, for example, whether enough disk space is
            /// available to write a stream to a file.
            /// This value should be considered a hint only; for some stream filters, it may not be possible
            /// to determine this value precisely.
            /// </summary>
            [PdfKey("1.5", PdfKeyType.Integer | PdfKeyType.Optional)]
            public const string DL = "/DL";

            // ReSharper restore InconsistentNaming
        }
    }
}
