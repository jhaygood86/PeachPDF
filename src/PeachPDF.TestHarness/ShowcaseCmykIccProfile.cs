/// <summary>
/// A minimal, spec-valid (ICC.1:2010) synthetic CMYK ICC profile for the PDF/X-1a showcase's
/// <c>ColorOptions.OutputIntentProfile</c> - a real-world press profile (SWOP/FOGRA/GRACoL) is typically
/// hundreds of KB to several MB of LUT tables, far too large to embed here. Same minimal <c>A2B0</c>/
/// <c>mft1</c> LUT8 shape as <c>PeachPDF.Tests.TestSupport.IccProfileFixture.BuildCmykProfile</c> (not
/// referenced directly - that helper is internal to the test assembly), except the CLUT's 16 grid corners
/// (2 points per input channel) are populated with the naive CMYK-&gt;RGB formula's values (reused here only
/// as a stand-in for real XYZ colorimetry, since the profile's own output curves are the identity - not
/// the naive-conversion policy this project otherwise avoids for real color writing) instead of being left
/// zeroed. A reader that actually applies this OutputIntent for CMYK color management (e.g. MuPDF, once
/// the document carries valid <c>GTS_PDFXVersion</c> identification - see
/// <c>PdfSharpCore.Pdf.Advanced.PdfMetadataStream.PdfXIdentifiers</c>) would otherwise map every CMYK
/// input to the same (black) output through an all-zero CLUT, which looked like a real PeachPDF rendering
/// bug until traced to this fixture - see this session's recent-fix note.
/// </summary>
internal static class ShowcaseCmykIccProfile
{
    private const int HeaderSize = 128;

    internal static byte[] Bytes { get; } = Build();

    private static byte[] Build()
    {
        const int inputChannels = 4;
        const int outputChannels = 3;
        const int clutGridPoints = 2;

        var lut = new byte[4 + 4 + 1 + 1 + 1 + 1 + 36 + (inputChannels * 256) + (IntPow(clutGridPoints, inputChannels) * outputChannels) + (outputChannels * 256)];
        var offset = 0;
        WriteAscii4(lut, offset, "mft1"); offset += 4;
        offset += 4; // reserved
        lut[offset++] = inputChannels;
        lut[offset++] = outputChannels;
        lut[offset++] = clutGridPoints;
        offset++; // padding
        offset += 36; // e1-e9 matrix - only meaningful for an RGB "prtr" profile, unused for CMYK

        for (var c = 0; c < inputChannels; c++)
        {
            WriteIdentityCurve(lut, offset);
            offset += 256;
        }

        WriteCmykClutCorners(lut, offset, outputChannels);
        offset += IntPow(clutGridPoints, inputChannels) * outputChannels;

        for (var c = 0; c < outputChannels; c++)
        {
            WriteIdentityCurve(lut, offset);
            offset += 256;
        }

        var tags = new (string Signature, byte[] Data)[] { ("A2B0", lut) };
        return BuildProfile("prtr", "CMYK", tags);
    }

    private static byte[] BuildProfile(string profileClass, string dataColorSpace, (string Signature, byte[] Data)[] tags)
    {
        var tagTableSize = 4 + (tags.Length * 12);
        var tagTableStart = HeaderSize;
        var tagDataStart = tagTableStart + tagTableSize;

        var offsets = new int[tags.Length];
        var cursor = tagDataStart;
        for (var i = 0; i < tags.Length; i++)
        {
            offsets[i] = cursor;
            cursor += tags[i].Data.Length;
        }

        var buffer = new byte[cursor];

        WriteUInt32(buffer, 0, (uint)buffer.Length);
        WriteAscii4(buffer, 12, profileClass);
        WriteAscii4(buffer, 16, dataColorSpace);
        WriteAscii4(buffer, 20, "XYZ ");
        WriteAscii4(buffer, 36, "acsp"); // ProfileFileSignature - required.
        WriteUInt32(buffer, 64, 0); // Intent: Perceptual.

        WriteUInt32(buffer, tagTableStart, (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entryOffset = tagTableStart + 4 + (i * 12);
            WriteAscii4(buffer, entryOffset, tags[i].Signature);
            WriteUInt32(buffer, entryOffset + 4, (uint)offsets[i]);
            WriteUInt32(buffer, entryOffset + 8, (uint)tags[i].Data.Length);
            System.Array.Copy(tags[i].Data, 0, buffer, offsets[i], tags[i].Data.Length);
        }

        return buffer;
    }

    /// <summary>
    /// Writes each of a 2-point-per-channel CLUT's 16 CMYK grid corners (every combination of C/M/Y/K each
    /// 0 or 1) as a real CIE XYZ (D50) triple - the naive CMYK-&gt;RGB formula's value converted through the
    /// standard sRGB-&gt;XYZ(D50) matrix and encoded per ICC.1:2010 §6.3.4.2's <c>lut8Type</c> PCSXYZ byte
    /// encoding (range [0, 65535/32768) maps linearly to a byte) - not the naive RGB bytes reused directly
    /// as if they were already XYZ, which a real ICC engine (e.g. MuPDF, once the document carries valid
    /// <c>GTS_PDFXVersion</c> identification) decodes as wildly different, wrong-looking hues. Storage
    /// order: the last input channel (K) varies fastest, then Y, then M, then C slowest (ICC.1:2010 §10.13).
    /// </summary>
    private static void WriteCmykClutCorners(byte[] buffer, int offset, int outputChannels)
    {
        // sRGB (D65) -> XYZ (D50), Bradford-adapted - the standard combined matrix (e.g. as used by
        // Little CMS/ICC reference profiles) applied directly to the naive formula's un-gamma-corrected
        // 0..1 values, since this fixture only needs a plausible, non-degenerate hue - not colorimetric
        // accuracy a real press profile would provide.
        const double m00 = 0.4360747, m01 = 0.3850649, m02 = 0.1430804;
        const double m10 = 0.2225045, m11 = 0.7168786, m12 = 0.0606169;
        const double m20 = 0.0139322, m21 = 0.0971045, m22 = 0.7141733;
        const double pcsXyzRange = 65535.0 / 32768.0; // lut8Type PCSXYZ encoding range, ICC.1:2010 §6.3.4.2

        for (var c = 0; c < 2; c++)
        {
            for (var m = 0; m < 2; m++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var k = 0; k < 2; k++)
                    {
                        var r = (1 - c) * (1 - k);
                        var g = (1 - m) * (1 - k);
                        var b = (1 - y) * (1 - k);

                        var x = m00 * r + m01 * g + m02 * b;
                        var yy = m10 * r + m11 * g + m12 * b;
                        var z = m20 * r + m21 * g + m22 * b;

                        var index = ((c * 2 + m) * 2 + y) * 2 + k;
                        var entryOffset = offset + index * outputChannels;
                        buffer[entryOffset] = EncodePcsXyzByte(x, pcsXyzRange);
                        buffer[entryOffset + 1] = EncodePcsXyzByte(yy, pcsXyzRange);
                        buffer[entryOffset + 2] = EncodePcsXyzByte(z, pcsXyzRange);
                    }
                }
            }
        }
    }

    private static byte EncodePcsXyzByte(double xyzComponent, double pcsXyzRange) =>
        (byte)System.Math.Clamp(System.Math.Round(xyzComponent * 255.0 / pcsXyzRange), 0, 255);

    private static void WriteIdentityCurve(byte[] buffer, int offset)
    {
        for (var i = 0; i < 256; i++)
        {
            buffer[offset + i] = (byte)i;
        }
    }

    private static int IntPow(int value, int exponent)
    {
        var result = 1;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }

    private static void WriteAscii4(byte[] buffer, int offset, string signature)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(signature);
        System.Array.Copy(bytes, 0, buffer, offset, 4);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
