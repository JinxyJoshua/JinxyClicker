using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// Reads and writes the two Windows pointer settings people mean by "DPI" and
/// "acceleration": the pointer-speed slider (1–20, where 10 is the 1:1 middle) and
/// "Enhance pointer precision". Both are system-wide, so the caller remembers the
/// originals and puts them back.
/// </summary>
public static class MouseSettings
{
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_SENDCHANGE = 0x0002;

    /// <summary>The pointer-speed slider, 1–20 (10 = no scaling).</summary>
    public static int GetPointerSpeed()
    {
        int speed = 10;
        SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0);
        return speed;
    }

    public static void SetPointerSpeed(int speed)
    {
        speed = Math.Clamp(speed, 1, 20);
        SystemParametersInfoSet(SPI_SETMOUSESPEED, 0, (IntPtr)speed, SPIF_SENDCHANGE);
    }

    /// <summary>Whether "Enhance pointer precision" (mouse acceleration) is on.</summary>
    public static bool GetAcceleration()
    {
        int[] mouse = new int[3];
        SystemParametersInfoArr(SPI_GETMOUSE, 0, mouse, 0);
        return mouse[2] != 0;   // the third param is the accel on/off switch
    }

    public static void SetAcceleration(bool on)
    {
        // Windows' own defaults for the two thresholds + the enable flag.
        int[] mouse = on ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
        SystemParametersInfoArr(SPI_SETMOUSE, 0, mouse, SPIF_SENDCHANGE);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
    private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
    private static extern bool SystemParametersInfoArr(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);
}
