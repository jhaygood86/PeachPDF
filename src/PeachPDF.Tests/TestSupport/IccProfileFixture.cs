using System;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Hand-builds minimal, spec-valid ICC profiles (ICC.1:2010) byte-for-byte, so tests can exercise
    /// PeachPDF's ICC-profile-extraction/embedding path (<c>PeachImageSource</c>/<c>PdfImage</c>) with a
    /// profile <c>PeachImage.IccColorProfile</c> actually parses successfully - real-world CMYK/RGB ICC
    /// profiles are typically hundreds of KB to several MB (LUT tables), far too large to embed as a test
    /// fixture. Mirrors PeachImage's own internal <c>SyntheticIccProfileBuilder</c> (not usable here - it's
    /// internal to PeachImage's own test assembly, a separate package), extended with a CMYK LUT8
    /// (<c>A2B0</c>/<c>mft1</c>) variant - a CMYK profile can never use PeachImage's simpler RGB-matrix-TRC
    /// shape (ICC.1:2010 §6.3.1.2 - matrix/TRC is only valid for a 3-channel RGB device space), so this is
    /// the smallest profile shape PeachImage's ICC engine (<c>IccTransformAToB</c>) accepts for CMYK.
    /// Colorimetric accuracy of the built profiles doesn't matter for these tests - only that
    /// <see cref="PeachImage.IccColorProfile.TryCreate"/>/<c>ImageMetadata.GetIccColorProfile()</c> parse
    /// them successfully with the expected <c>DataColorSpace</c>/<c>ChannelCount</c>.
    /// </summary>
    internal static class IccProfileFixture
    {
        private const int HeaderSize = 128;

        /// <summary>
        /// A minimal RGB-matrix-TRC profile: linear rTRC/gTRC/bTRC curves plus the standard sRGB primaries
        /// (D50-adapted XYZ) - same shape as PeachImage's own <c>SyntheticIccProfileBuilder.BuildRgbTrcMatrixProfile</c>.
        /// </summary>
        internal static byte[] BuildRgbProfile()
        {
            var tags = new (string Signature, byte[] Data)[]
            {
                ("rTRC", BuildLinearCurve()),
                ("gTRC", BuildLinearCurve()),
                ("bTRC", BuildLinearCurve()),
                ("rXYZ", BuildXyzType(0.4360747, 0.2225045, 0.0139322)),
                ("gXYZ", BuildXyzType(0.3850649, 0.7168786, 0.0971045)),
                ("bXYZ", BuildXyzType(0.1430804, 0.0606169, 0.7141733)),
            };

            return BuildProfile("mntr", "RGB ", tags);
        }

        /// <summary>A minimal single-curve grey profile: a linear <c>kTRC</c> curve.</summary>
        internal static byte[] BuildGrayProfile()
        {
            var tags = new (string Signature, byte[] Data)[]
            {
                ("kTRC", BuildLinearCurve()),
            };

            return BuildProfile("mntr", "GRAY", tags);
        }

        /// <summary>
        /// A minimal CMYK profile: a single <c>A2B0</c> tag using the 8-bit LUT (<c>mft1</c>) shape - input
        /// curves (identity), a 2-grid-point-per-dimension CLUT (16 entries for 4 input channels), and
        /// output curves (identity). The CLUT's actual C/M/Y/K-&gt;XYZ mapping is arbitrary (zeroed) -
        /// nothing in PeachPDF's own pipeline calls <see cref="PeachImage.IccColorProfile.ConvertToSrgb"/>
        /// on the fixtures built here, so only successful parsing (not conversion accuracy) is exercised.
        /// </summary>
        internal static byte[] BuildCmykProfile()
        {
            var tags = new (string Signature, byte[] Data)[] { ("A2B0", BuildMft1Lut(4, 3, 2)) };
            return BuildProfile("prtr", "CMYK", tags);
        }

        /// <summary>
        /// Same shape as <see cref="BuildCmykProfile"/>, plus a <c>B2A0</c> tag (PCS-&gt;device, the same
        /// 8-bit LUT shape with input/output channel counts swapped) - needed only when the profile is
        /// used as a <see cref="PeachImage.IccColorProfile.ConvertTo"/> *destination* (the reverse
        /// direction), which <see cref="BuildCmykProfile"/>'s <c>A2B0</c>-only shape can't satisfy
        /// (<c>ConvertTo</c> throws <see cref="NotSupportedException"/> for a destination with no BToA
        /// transform - the same failure a real AToB-only scanner/input profile would hit).
        /// </summary>
        internal static byte[] BuildCmykProfileWithReverseTransform()
        {
            var tags = new (string Signature, byte[] Data)[]
            {
                ("A2B0", BuildMft1Lut(4, 3, 2)),
                ("B2A0", BuildMft1Lut(3, 4, 2)),
            };
            return BuildProfile("prtr", "CMYK", tags);
        }

        /// <summary>
        /// An <c>mft1</c> (8-bit LUT) tag body (ICC.1:2010 §10.9): input curves (identity), a
        /// <paramref name="clutGridPoints"/>-per-dimension CLUT (zeroed - see <see cref="BuildCmykProfile"/>'s
        /// doc comment on why accuracy doesn't matter for these fixtures), and output curves (identity).
        /// </summary>
        private static byte[] BuildMft1Lut(int inputChannels, int outputChannels, int clutGridPoints)
        {
            var lut = new byte[4 + 4 + 1 + 1 + 1 + 1 + 36 + (inputChannels * 256) + (IntPow(clutGridPoints, inputChannels) * outputChannels) + (outputChannels * 256)];
            int offset = 0;
            WriteAscii4(lut, offset, "mft1"); offset += 4;
            offset += 4; // reserved
            lut[offset++] = (byte)inputChannels;
            lut[offset++] = (byte)outputChannels;
            lut[offset++] = (byte)clutGridPoints;
            offset++; // padding
            offset += 36; // e1-e9 matrix - only meaningful for an RGB "prtr" profile, unused here; left zeroed (identity-adjacent, never read for CMYK)

            for (int c = 0; c < inputChannels; c++)
            {
                WriteIdentityCurve(lut, offset);
                offset += 256;
            }

            // CLUT values left zeroed - see BuildCmykProfile's doc comment on why accuracy doesn't matter here.
            offset += IntPow(clutGridPoints, inputChannels) * outputChannels;

            for (int c = 0; c < outputChannels; c++)
            {
                WriteIdentityCurve(lut, offset);
                offset += 256;
            }

            return lut;
        }

        private static byte[] BuildProfile(string profileClass, string dataColorSpace, (string Signature, byte[] Data)[] tags)
        {
            int tagTableSize = 4 + (tags.Length * 12);
            int tagTableStart = HeaderSize;
            int tagDataStart = tagTableStart + tagTableSize;

            var offsets = new int[tags.Length];
            int cursor = tagDataStart;
            for (int i = 0; i < tags.Length; i++)
            {
                offsets[i] = cursor;
                cursor += tags[i].Data.Length;
            }

            int totalSize = cursor;
            var buffer = new byte[totalSize];

            // Header (ICC.1:2010 §7.2) - fields PeachImage's IccHeader never reads (CMM type, version
            // detail, dates, platform/flags/manufacturer/attributes, PCS illuminant, creator, profile id)
            // are left zeroed, same as PeachImage's own SyntheticIccProfileBuilder.
            WriteUInt32(buffer, 0, (uint)totalSize);
            WriteAscii4(buffer, 12, profileClass);
            WriteAscii4(buffer, 16, dataColorSpace);
            WriteAscii4(buffer, 20, "XYZ ");
            WriteAscii4(buffer, 36, "acsp"); // ProfileFileSignature - required.
            WriteUInt32(buffer, 64, 0); // Intent: Perceptual.

            // Tag table (§7.3).
            WriteUInt32(buffer, tagTableStart, (uint)tags.Length);
            for (int i = 0; i < tags.Length; i++)
            {
                int entryOffset = tagTableStart + 4 + (i * 12);
                WriteAscii4(buffer, entryOffset, tags[i].Signature);
                WriteUInt32(buffer, entryOffset + 4, (uint)offsets[i]);
                WriteUInt32(buffer, entryOffset + 8, (uint)tags[i].Data.Length);
                Array.Copy(tags[i].Data, 0, buffer, offsets[i], tags[i].Data.Length);
            }

            return buffer;
        }

        /// <summary>A <c>curv</c> tag (§10.5) with a single gamma=1.0 entry - a linear (identity) tone curve.</summary>
        private static byte[] BuildLinearCurve()
        {
            var data = new byte[14];
            WriteAscii4(data, 0, "curv");
            WriteUInt32(data, 8, 1); // entry count = 1 -> single gamma value follows.
            WriteU8Fixed8(data, 12, 1.0); // gamma = 1.0 (linear).
            return data;
        }

        /// <summary>An <c>XYZType</c> tag (§10.24).</summary>
        private static byte[] BuildXyzType(double x, double y, double z)
        {
            var data = new byte[20];
            WriteAscii4(data, 0, "XYZ ");
            WriteS15Fixed16(data, 8, x);
            WriteS15Fixed16(data, 12, y);
            WriteS15Fixed16(data, 16, z);
            return data;
        }

        /// <summary>Writes a 256-byte 8-bit identity curve (value[i] = i) at <paramref name="offset"/>.</summary>
        private static void WriteIdentityCurve(byte[] buffer, int offset)
        {
            for (int i = 0; i < 256; i++)
            {
                buffer[offset + i] = (byte)i;
            }
        }

        private static int IntPow(int value, int exponent)
        {
            int result = 1;
            for (int i = 0; i < exponent; i++)
            {
                result *= value;
            }

            return result;
        }

        private static void WriteAscii4(byte[] buffer, int offset, string signature)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(signature);
            Array.Copy(bytes, 0, buffer, offset, 4);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteU8Fixed8(byte[] buffer, int offset, double value)
        {
            byte integer = (byte)value;
            byte fraction = (byte)Math.Round((value - integer) * 256.0);
            buffer[offset] = integer;
            buffer[offset + 1] = fraction;
        }

        private static void WriteS15Fixed16(byte[] buffer, int offset, double value)
        {
            int fixedValue = (int)Math.Round(value * 65536.0);
            buffer[offset] = (byte)(fixedValue >> 24);
            buffer[offset + 1] = (byte)(fixedValue >> 16);
            buffer[offset + 2] = (byte)(fixedValue >> 8);
            buffer[offset + 3] = (byte)fixedValue;
        }

        /// <summary>
        /// Returns a copy of <paramref name="jpegBytes"/> with <paramref name="iccProfile"/> inserted as a
        /// single-chunk APP2 <c>ICC_PROFILE</c> marker segment (JPEG File Interchange Format APP2 ICC
        /// profile convention) immediately after the SOI marker - the simplest legal position, and the one
        /// PeachImage's own <c>FrameDecoder</c> collects APP2 chunks from regardless of exactly where they
        /// fall among the other header segments. Only handles a profile small enough for one chunk (under
        /// ~65KB) - both fixtures this file builds are a few KB, well within that.
        /// </summary>
        internal static byte[] InsertIccProfileIntoJpeg(byte[] jpegBytes, byte[] iccProfile)
        {
            if (jpegBytes.Length < 2 || jpegBytes[0] != 0xFF || jpegBytes[1] != 0xD8)
            {
                throw new ArgumentException("Expected a JPEG byte stream starting with the SOI marker (0xFFD8).", nameof(jpegBytes));
            }

            var signature = System.Text.Encoding.ASCII.GetBytes("ICC_PROFILE\0");
            int payloadLength = signature.Length + 2 + iccProfile.Length; // + sequence(1) + count(1)
            int segmentLength = payloadLength + 2; // + the length field itself
            if (segmentLength > ushort.MaxValue)
            {
                throw new ArgumentException($"ICC profile is too large for a single APP2 chunk ({iccProfile.Length} bytes).", nameof(iccProfile));
            }

            var result = new byte[2 + 2 + 2 + payloadLength + (jpegBytes.Length - 2)];
            int offset = 0;
            result[offset++] = 0xFF;
            result[offset++] = 0xD8; // SOI, copied from the source
            result[offset++] = 0xFF;
            result[offset++] = 0xE2; // APP2 marker
            result[offset++] = (byte)(segmentLength >> 8);
            result[offset++] = (byte)segmentLength;
            Array.Copy(signature, 0, result, offset, signature.Length);
            offset += signature.Length;
            result[offset++] = 1; // chunk sequence (1-based)
            result[offset++] = 1; // total chunk count
            Array.Copy(iccProfile, 0, result, offset, iccProfile.Length);
            offset += iccProfile.Length;
            Array.Copy(jpegBytes, 2, result, offset, jpegBytes.Length - 2);

            return result;
        }

        /// <summary>
        /// Returns a copy of a simple-format <paramref name="webpBytes"/> (a bare <c>RIFF...WEBP VP8 </c>
        /// or <c>VP8L</c> chunk, as PeachImage's own encoder produces - see <c>WebpEncoderOptions</c>)
        /// upgraded to the extended (<c>VP8X</c>) container format with <paramref name="iccProfile"/>
        /// embedded as an <c>ICCP</c> chunk - the real-world shape a WebP with a color profile actually
        /// takes (RIFF WEBP VP8X [ICCP] VP8/VP8L, per the WebP container spec's chunk ordering).
        /// </summary>
        internal static byte[] InsertIccProfileIntoWebp(byte[] webpBytes, byte[] iccProfile)
        {
            if (webpBytes.Length < 12 ||
                webpBytes[0] != (byte)'R' || webpBytes[1] != (byte)'I' || webpBytes[2] != (byte)'F' || webpBytes[3] != (byte)'F' ||
                webpBytes[8] != (byte)'W' || webpBytes[9] != (byte)'E' || webpBytes[10] != (byte)'B' || webpBytes[11] != (byte)'P')
            {
                throw new ArgumentException("Expected a simple-format WebP byte stream (RIFF....WEBP...).", nameof(webpBytes));
            }

            var info = PeachImage.Image.Identify(new System.IO.MemoryStream(webpBytes));
            string fourCc = System.Text.Encoding.ASCII.GetString(webpBytes, 12, 4);
            uint chunkSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(webpBytes.AsSpan(16, 4));
            int chunkTotal = 8 + (int)chunkSize + (int)(chunkSize % 2); // header + data + even padding
            var originalChunk = webpBytes.AsSpan(12, chunkTotal).ToArray();

            var vp8xData = new byte[10];
            vp8xData[0] = 0x20; // ICC flag bit
            WriteUInt24LittleEndian(vp8xData.AsSpan(4), info.Width - 1);
            WriteUInt24LittleEndian(vp8xData.AsSpan(7), info.Height - 1);

            using var result = new System.IO.MemoryStream();
            result.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            result.Write(new byte[4]); // size placeholder, patched below
            result.Write(System.Text.Encoding.ASCII.GetBytes("WEBP"));
            WriteRiffChunk(result, "VP8X", vp8xData);
            WriteRiffChunk(result, "ICCP", iccProfile);
            result.Write(originalChunk);

            var bytes = result.ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
            return bytes;

            static void WriteUInt24LittleEndian(Span<byte> destination, int value)
            {
                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
            }
        }

        private static void WriteRiffChunk(System.IO.Stream stream, string fourCc, byte[] data)
        {
            stream.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
            Span<byte> sizeBytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)data.Length);
            stream.Write(sizeBytes);
            stream.Write(data);
            if (data.Length % 2 != 0)
            {
                stream.WriteByte(0); // RIFF chunks are padded to an even length
            }
        }

        /// <summary>
        /// Returns a copy of <paramref name="pngBytes"/> (produced by a real encoder - see
        /// <c>RasterPngFixture</c>'s own reasoning for why) with <paramref name="iccProfile"/> inserted as
        /// an <c>iCCP</c> chunk (profile name "icc\0", compression method 0, zlib-deflated profile data)
        /// immediately after the IHDR chunk - the position PNG's own chunk-ordering rules require (before
        /// PLTE/IDAT). PeachImage's <c>PngPassthrough</c>/full-decode paths both accept <c>iCCP</c>
        /// anywhere before <c>IDAT</c>, but this is also the canonical position real encoders use.
        /// </summary>
        internal static byte[] InsertIccProfileIntoPng(byte[] pngBytes, byte[] iccProfile)
        {
            const int SignatureLength = 8;
            if (pngBytes.Length < SignatureLength + 8 || pngBytes[0] != 0x89 || pngBytes[1] != (byte)'P')
            {
                throw new ArgumentException("Expected a PNG byte stream starting with the 8-byte PNG signature.", nameof(pngBytes));
            }

            uint ihdrLength = (uint)((pngBytes[8] << 24) | (pngBytes[9] << 16) | (pngBytes[10] << 8) | pngBytes[11]);
            int ihdrChunkEnd = SignatureLength + 4 + 4 + (int)ihdrLength + 4; // length + type + data + CRC

            using var compressed = new System.IO.MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(iccProfile);
            }

            var profileName = System.Text.Encoding.ASCII.GetBytes("icc");
            var chunkData = new byte[profileName.Length + 1 + 1 + compressed.Length];
            int pos = 0;
            Array.Copy(profileName, 0, chunkData, pos, profileName.Length);
            pos += profileName.Length;
            chunkData[pos++] = 0; // null terminator after the profile name
            chunkData[pos++] = 0; // compression method: zlib/deflate
            Array.Copy(compressed.ToArray(), 0, chunkData, pos, compressed.Length);

            using var result = new System.IO.MemoryStream();
            result.Write(pngBytes, 0, ihdrChunkEnd);
            WriteChunk(result, "iCCP", chunkData);
            result.Write(pngBytes, ihdrChunkEnd, pngBytes.Length - ihdrChunkEnd);

            return result.ToArray();
        }

        private static void WriteChunk(System.IO.Stream stream, string type, byte[] data)
        {
            Span<byte> lengthBytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
            stream.Write(lengthBytes);

            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes);
            stream.Write(data);

            var crcInput = new byte[typeBytes.Length + data.Length];
            typeBytes.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, typeBytes.Length);

            Span<byte> crcBytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(crcInput));
            stream.Write(crcBytes);
        }

        // Standard PNG/zlib CRC-32 (polynomial 0xEDB88320, reflected) - same table-driven implementation
        // as RasterPngFixture's own (kept separate rather than shared, since both are small, self-contained
        // test-fixture helpers with no other coupling between the two files).
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}
