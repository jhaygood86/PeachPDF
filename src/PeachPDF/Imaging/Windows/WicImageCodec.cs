using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Windows
{
    /// <summary>
    /// Native decode/encode for Windows via WIC (Windows Imaging Component). "Does WIC support this
    /// format" is answered by simply attempting <see cref="IWICImagingFactory.CreateDecoderFromStream"/>
    /// and treating any failing HRESULT (surfaced as an exception by the generated COM marshalling) as
    /// unsupported - not a separate capability query. Covers JPEG/PNG/GIF/BMP/TIFF always, and WebP/AVIF
    /// opportunistically if the OS has the optional codec packs installed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed unsafe class WicImageCodec : IPlatformImageDecoder, IPlatformImageEncoder
    {
        private const uint ClsCtxInprocServer = 1;
        private const int WicDecodeMetadataCacheOnDemand = 0;
        private const int WicBitmapDitherTypeNone = 0;
        private const int WicBitmapPaletteTypeCustom = 0;
        private const int WicBitmapEncoderNoCache = 2;
        private const int StatFlagNoName = 1;

        private static readonly StrategyBasedComWrappers Wrappers = new();

        // WIC's IWICImagingFactory is shared process-wide (one PdfGenerator per thread is this project's
        // documented supported concurrency model - see docs/architecture.md's Thread safety section - so
        // concurrent PdfGenerator instances on different threads all reach the same factory instance).
        // .NET's thread pool never calls CoInitializeEx, so a thread's COM apartment state is otherwise
        // undefined the first time it reaches WIC - the actual root cause of an intermittent decode/encode
        // failure observed empirically under heavy parallel xUnit execution (thousands of tests across
        // many classes hitting this codec concurrently), which stopped reproducing once every calling
        // thread explicitly joined the multi-threaded apartment below.
        //
        // In theory no caller-side lock should be needed once every thread has a proper apartment: as the
        // *consumer* of WIC (not a codec author), COM's own apartment infrastructure is what has to
        // guarantee thread safety for a properly-initialized calling thread, and Microsoft documents the
        // in-box factory/codecs (JPEG/TIFF/PNG/GIF/ICO/BMP) as updated to support concurrent MTA access.
        // Empirically, though, CoInitializeEx alone did NOT eliminate an intermittent failure under heavy
        // parallel xUnit execution (thousands of tests hitting this codec concurrently) - it reproduced
        // identically with no lock at all, and stopped only once the calls that go through the shared
        // `_factory` instance specifically were serialized (below). That points at the WebP/AVIF Store
        // codec pack's own internal state (reached via the factory's format-lookup/creation dispatch) not
        // actually being safe for concurrent access despite Microsoft's guidance that 3rd-party codecs are
        // supposed to implement that themselves - this lock is a pragmatic workaround for that on the
        // consumer side, scoped as narrowly as the evidence supports (only the factory calls, not the
        // whole decode/encode - decoder/frame/converter/encoder objects are independent per call).
        private static readonly object FactorySyncRoot = new();

        [ThreadStatic]
        private static bool _threadJoinedMta;

        private static void EnsureMultithreadedApartment()
        {
            if (_threadJoinedMta) return;

            var hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.CoInitMultithreaded);
            if (hr >= 0 || hr == Ole32.RpcEChangedMode)
            {
                // Either this thread is now MTA, or it already had an apartment - both are terminal
                // states for this thread, so there's no point calling CoInitializeEx on it again.
                _threadJoinedMta = true;
            }
        }

        private readonly IWICImagingFactory? _factory;

        public WicImageCodec()
        {
            _factory = CreateFactory();
        }

        private static IWICImagingFactory? CreateFactory()
        {
            try
            {
                var iid = typeof(IWICImagingFactory).GUID;
                if (Ole32.CoCreateInstance(WicGuids.ClsidWicImagingFactory, IntPtr.Zero, ClsCtxInprocServer, iid, out var raw) < 0 || raw == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return (IWICImagingFactory)Wrappers.GetOrCreateObjectForComInstance(raw, CreateObjectFlags.None);
                }
                finally
                {
                    Marshal.Release(raw);
                }
            }
            catch
            {
                return null;
            }
        }

        public bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedImage result)
        {
            result = default;
            if (_factory is null) return false;

            EnsureMultithreadedApartment();

            try
            {
                IWICStream wicStream;
                IWICBitmapSource frame;
                IWICFormatConverter converter;

                // Only the calls that go through the shared _factory instance are locked - everything
                // else below operates on objects freshly created per call, never touched by another
                // thread. See the field-level comment for why this narrow scope, not the whole method,
                // is what empirical testing showed is actually necessary.
                lock (FactorySyncRoot)
                {
                    _factory.CreateStream(out wicStream);

                    fixed (byte* ptr = bytes)
                    {
                        wicStream.InitializeFromMemory((IntPtr)ptr, (uint)bytes.Length);
                    }

                    _factory.CreateDecoderFromStream((IStreamCom)wicStream, IntPtr.Zero, WicDecodeMetadataCacheOnDemand, out var decoder);
                    decoder.GetFrame(0, out frame);

                    _factory.CreateFormatConverter(out converter);
                }

                converter.Initialize(frame, WicGuids.GuidWicPixelFormat32bppRgba, WicBitmapDitherTypeNone, IntPtr.Zero, 0.0, WicBitmapPaletteTypeCustom);

                converter.GetSize(out var width, out var height);
                if (width == 0 || height == 0) return false;

                var stride = width * 4;
                var bufferSize = (int)(stride * height);
                var buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    converter.CopyPixels(IntPtr.Zero, stride, (uint)bufferSize, buffer);
                    var managed = new byte[bufferSize];
                    Marshal.Copy(buffer, managed, 0, bufferSize);
                    result = DecodedImage.FromRgba(managed, (int)width, (int)height);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                result = default;
                return false;
            }
        }

        public bool TryEncodeJpeg(in DecodedImage image, int quality, out byte[] jpegBytes)
        {
            return TryEncode(image, WicGuids.GuidContainerFormatJpeg, WicGuids.GuidWicPixelFormat24bppBgr, out jpegBytes);
        }

        public bool TryEncodeBmp(in DecodedImage image, out byte[] bmpBytes)
        {
            return TryEncode(image, WicGuids.GuidContainerFormatBmp, WicGuids.GuidWicPixelFormat32bppBgra, out bmpBytes);
        }

        private bool TryEncode(in DecodedImage image, Guid containerFormat, Guid requestedPixelFormat, out byte[] encodedBytes)
        {
            encodedBytes = [];
            if (_factory is null) return false;

            EnsureMultithreadedApartment();

            var outputStreamPtr = IntPtr.Zero;

            try
            {
                outputStreamPtr = Shlwapi.SHCreateMemStream(IntPtr.Zero, 0);
                if (outputStreamPtr == IntPtr.Zero) return false;

                var outputStream = (IStreamCom)Wrappers.GetOrCreateObjectForComInstance(outputStreamPtr, CreateObjectFlags.None);

                IWICBitmapEncoder encoder;
                lock (FactorySyncRoot)
                {
                    _factory.CreateEncoder(containerFormat, IntPtr.Zero, out encoder);
                }

                encoder.Initialize(outputStream, WicBitmapEncoderNoCache);

                encoder.CreateNewFrame(out var frameEncode, IntPtr.Zero);
                frameEncode.Initialize(IntPtr.Zero);
                frameEncode.SetSize((uint)image.Width, (uint)image.Height);

                var actualFormat = requestedPixelFormat;
                frameEncode.SetPixelFormat(ref actualFormat);
                if (actualFormat != requestedPixelFormat)
                {
                    // WIC substituted a format this code doesn't know how to pack pixels for - fail
                    // closed (falls back to StbImageWriteSharp) rather than write mismatched pixel data.
                    return false;
                }

                var isBgra = requestedPixelFormat == WicGuids.GuidWicPixelFormat32bppBgra;
                var bytesPerPixel = isBgra ? 4 : 3;
                var stride = (uint)(image.Width * bytesPerPixel);
                var packed = PackBgr(image.Rgba, image.Width, image.Height, isBgra);

                var handle = GCHandle.Alloc(packed, GCHandleType.Pinned);
                try
                {
                    frameEncode.WritePixels((uint)image.Height, stride, (uint)packed.Length, handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }

                frameEncode.Commit();
                encoder.Commit();

                outputStream.Seek(0, 0, out _);
                outputStream.Stat(out var stat, StatFlagNoName);
                var length = (int)stat.CbSize;

                var resultBuffer = Marshal.AllocHGlobal(length);
                try
                {
                    outputStream.Read(resultBuffer, (uint)length, out var bytesRead);
                    encodedBytes = new byte[bytesRead];
                    Marshal.Copy(resultBuffer, encodedBytes, 0, (int)bytesRead);
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(resultBuffer);
                }
            }
            catch
            {
                encodedBytes = [];
                return false;
            }
            finally
            {
                if (outputStreamPtr != IntPtr.Zero) Marshal.Release(outputStreamPtr);
            }
        }

        /// <summary>
        /// Converts our tightly-packed RGBA buffer into tightly-packed BGR (JPEG - alpha dropped) or BGRA
        /// (BMP), the byte orders WIC's built-in encoders reliably accept.
        /// </summary>
        private static byte[] PackBgr(byte[] rgba, int width, int height, bool keepAlpha)
        {
            var bytesPerPixel = keepAlpha ? 4 : 3;
            var packed = new byte[width * height * bytesPerPixel];

            var srcIndex = 0;
            var dstIndex = 0;
            for (var i = 0; i < width * height; i++)
            {
                var r = rgba[srcIndex];
                var g = rgba[srcIndex + 1];
                var b = rgba[srcIndex + 2];

                packed[dstIndex] = b;
                packed[dstIndex + 1] = g;
                packed[dstIndex + 2] = r;
                if (keepAlpha)
                {
                    packed[dstIndex + 3] = rgba[srcIndex + 3];
                }

                srcIndex += 4;
                dstIndex += bytesPerPixel;
            }

            return packed;
        }
    }
}
