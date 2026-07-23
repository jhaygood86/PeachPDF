using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace PeachPDF.Imaging.Windows
{
    /// <summary>
    /// Minimal WIC (Windows Imaging Component) COM surface, via System.Runtime.InteropServices.Marshalling's
    /// [GeneratedComInterface] source generator (not classic [ComImport]/manual vtables), per the project's
    /// AOT/trim-safety requirement. Each interface declares only its vtable slots up through the last
    /// method actually called - the real methods before that point, in the real order, verified directly
    /// against the Windows SDK's wincodec.h/objidl.h (not recalled from memory), since a wrong vtable ORDER
    /// (unlike a wrong GUID, which just fails QueryInterface gracefully) causes undefined behavior.
    /// Methods not implemented here still occupy a correctly-shaped slot (an IntPtr/Guid/scalar parameter
    /// list matching the real ABI) so later, needed methods land on the right vtable index.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class WicGuids
    {
        // The original (Vista+) WIC factory CLSID - broadest compatible, per this project's "use only the
        // original stable WIC v1 interfaces" decision.
        public static readonly Guid ClsidWicImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");

        // IWICImagingFactory::CreateEncoder takes a GUID_ContainerFormatXxx value (the format itself),
        // NOT an encoder CLSID (e.g. CLSID_WICJpegEncoder) - passing a CLSID here fails with
        // WINCODEC_ERR_COMPONENTNOTFOUND, caught empirically via a local Windows test run.
        public static readonly Guid GuidContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
        public static readonly Guid GuidContainerFormatBmp = new("0af1d87e-fcfe-4188-bdeb-a7906471cbe3");

        public static readonly Guid GuidWicPixelFormat32bppRgba = new("f5c7ad2d-6a8d-43dd-a7a8-a29935261ae9");

        // Native/traditional Windows byte order (B,G,R[,A]) - requested for encoding since it's the
        // format WIC's built-in JPEG/BMP encoders are most reliably guaranteed to accept without silently
        // substituting a different one via SetPixelFormat's in/out negotiation. Verified directly against
        // the installed Windows SDK's wincodec.h (not recalled from memory) after an initial empirical
        // test run caught a wrong pair of guesses here (which WIC's own format negotiation duly rejected).
        public static readonly Guid GuidWicPixelFormat24bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
        public static readonly Guid GuidWicPixelFormat32bppBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("0000000c-0000-0000-C000-000000000046")]
    internal partial interface IStreamCom
    {
        void Read(IntPtr pv, uint cb, out uint pcbRead);
        void Write(IntPtr pv, uint cb, out uint pcbWritten);
        void Seek(long dlibMove, uint dwOrigin, out ulong plibNewPosition);
        void SetSize(ulong libNewSize);
        void CopyTo(IntPtr pstm, ulong cb, out ulong pcbRead, out ulong pcbWritten);
        void Commit(uint grfCommitFlags);
        void Revert();
        void LockRegion(ulong libOffset, ulong cb, uint dwLockType);
        void UnlockRegion(ulong libOffset, ulong cb, uint dwLockType);
        void Stat(out StatStg pstatstg, uint grfStatFlag);
        void Clone(out IStreamCom ppstm);
    }

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    internal struct StatStg
    {
        public IntPtr PwcsName;
        public uint Type;
        public ulong CbSize;
        public long MTime;
        public long CTime;
        public long ATime;
        public uint Grfmode;
        public uint GrfLocksSupported;
        public Guid Clsid;
        public uint GrfStateBits;
        public uint Reserved;
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("135FF860-22B7-4ddf-B0F6-218F4F299A43")]
    internal partial interface IWICStream : IStreamCom
    {
        void InitializeFromIStream(IStreamCom pIStream);
        void InitializeFromFilename([MarshalAs(UnmanagedType.LPWStr)] string wzFileName, uint dwDesiredAccess);
        void InitializeFromMemory(IntPtr pbBuffer, uint cbBufferSize);
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
    internal partial interface IWICBitmapSource
    {
        void GetSize(out uint puiWidth, out uint puiHeight);
        void GetPixelFormat(out Guid pPixelFormat);
        void GetResolution(out double pDpiX, out double pDpiY);
        void CopyPalette(IntPtr pIPalette);
        void CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
    internal partial interface IWICFormatConverter : IWICBitmapSource
    {
        void Initialize(IWICBitmapSource pISource, in Guid dstFormat, int dither, IntPtr pIPalette, double alphaThresholdPercent, int paletteTranslate);
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF")]
    internal partial interface IWICBitmapDecoder
    {
        void QueryCapability(IStreamCom pIStream, out uint pdwCapability);
        void Initialize(IStreamCom pIStream, int cacheOptions);
        void GetContainerFormat(out Guid pguidContainerFormat);
        void GetDecoderInfo(out IntPtr ppIDecoderInfo);
        void CopyPalette(IntPtr pIPalette);
        void GetMetadataQueryReader(out IntPtr ppIMetadataQueryReader);
        void GetPreview(out IntPtr ppIBitmapSource);
        void GetColorContexts(uint cCount, IntPtr ppIColorContexts, out uint pcActualCount);
        void GetThumbnail(out IntPtr ppIThumbnail);
        void GetFrameCount(out uint pCount);
        void GetFrame(uint index, out IWICBitmapSource ppIBitmapFrame);
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("00000105-a8f2-4877-ba0a-fd2b6645fb94")]
    internal partial interface IWICBitmapFrameEncode
    {
        void Initialize(IntPtr pIEncoderOptions);
        void SetSize(uint uiWidth, uint uiHeight);
        void SetResolution(double dpiX, double dpiY);
        void SetPixelFormat(ref Guid pPixelFormat);
        void SetColorContexts(uint cCount, IntPtr ppIColorContext);
        void SetPalette(IntPtr pIPalette);
        void SetThumbnail(IntPtr pIThumbnail);
        void WritePixels(uint lineCount, uint cbStride, uint cbBufferSize, IntPtr pbPixels);
        void WriteSource(IntPtr pIBitmapSource, IntPtr prc);
        void Commit();
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("00000103-a8f2-4877-ba0a-fd2b6645fb94")]
    internal partial interface IWICBitmapEncoder
    {
        void Initialize(IStreamCom pIStream, int cacheOption);
        void GetContainerFormat(out Guid pguidContainerFormat);
        void GetEncoderInfo(out IntPtr ppIEncoderInfo);
        void SetColorContexts(uint cCount, IntPtr ppIColorContext);
        void SetPalette(IntPtr pIPalette);
        void SetThumbnail(IntPtr pIThumbnail);
        void SetPreview(IntPtr pIPreview);
        void CreateNewFrame(out IWICBitmapFrameEncode ppIFrameEncode, IntPtr ppIEncoderOptions);
        void Commit();
    }

    [SupportedOSPlatform("windows")]
    [GeneratedComInterface]
    [Guid("EC5EC8A9-C395-4314-9C77-54D7A935FF70")]
    internal partial interface IWICImagingFactory
    {
        void CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string wzFilename, IntPtr pguidVendor, uint dwDesiredAccess, int metadataOptions, out IntPtr ppIDecoder);
        void CreateDecoderFromStream(IStreamCom pIStream, IntPtr pguidVendor, int metadataOptions, out IWICBitmapDecoder ppIDecoder);
        void CreateDecoderFromFileHandle(nuint hFile, IntPtr pguidVendor, int metadataOptions, out IntPtr ppIDecoder);
        void CreateComponentInfo(in Guid clsidComponent, out IntPtr ppIInfo);
        void CreateDecoder(in Guid guidContainerFormat, IntPtr pguidVendor, out IntPtr ppIDecoder);
        void CreateEncoder(in Guid guidContainerFormat, IntPtr pguidVendor, out IWICBitmapEncoder ppIEncoder);
        void CreatePalette(out IntPtr ppIPalette);
        void CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
        void CreateBitmapScaler(out IntPtr ppIBitmapScaler);
        void CreateBitmapClipper(out IntPtr ppIBitmapClipper);
        void CreateBitmapFlipRotator(out IntPtr ppIBitmapFlipRotator);
        void CreateStream(out IWICStream ppIWICStream);
    }

    [SupportedOSPlatform("windows")]
    internal static partial class Ole32
    {
        /// <summary>COINIT_MULTITHREADED - the calling thread joins/creates the multi-threaded apartment.</summary>
        public const uint CoInitMultithreaded = 0x0;

        /// <summary>RPC_E_CHANGED_MODE - the thread already has an incompatible apartment (e.g. STA).</summary>
        public const int RpcEChangedMode = unchecked((int)0x80010106);

        [LibraryImport("ole32.dll")]
        public static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

        [LibraryImport("ole32.dll")]
        public static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
    }

    [SupportedOSPlatform("windows")]
    internal static partial class Shlwapi
    {
        [LibraryImport("shlwapi.dll")]
        public static partial IntPtr SHCreateMemStream(IntPtr pInit, uint cbInit);
    }
}
