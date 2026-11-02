using System.Runtime.InteropServices;

namespace PeachDrawing.Win2D;

/// <summary>What Windows says about how text is smoothed on the user's displays: what a canvas that picks a text rendering for itself asks.</summary>
internal static unsafe class Win2dSystemText
{
    private const uint GetFontSmoothing = 0x004A;
    private const uint GetFontSmoothingType = 0x200A;
    private const uint GetFontSmoothingOrientation = 0x2012;
    private const uint ClearTypeSmoothing = 2;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    private static extern int SystemParametersInfo(uint action, uint param, void* value, uint winIni);

    /// <summary>Whether font smoothing is on and set to ClearType, so text on screen is expected to have colour fringes.</summary>
    public static bool ClearTypeEnabled
    {
        get
        {
            int smoothing = 0;
            uint type = 0;
            try
            {
                return SystemParametersInfo(GetFontSmoothing, 0, &smoothing, 0) != 0 && smoothing != 0
                    && SystemParametersInfo(GetFontSmoothingType, 0, &type, 0) != 0 && type == ClearTypeSmoothing;
            }
            catch (System.EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Whether the stripes of a pixel run blue, green, red from left to right: the user's ClearType setting, red first when it cannot be read.</summary>
    public static bool BgrStripes
    {
        get
        {
            uint orientation = 1;
            try
            {
                // 0 is BGR and 1 is RGB.
                return SystemParametersInfo(GetFontSmoothingOrientation, 0, &orientation, 0) != 0 && orientation == 0;
            }
            catch (System.EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
