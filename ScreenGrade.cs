using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// Applies a real colour grade to the whole screen through the Windows
/// Magnification API — the same mechanism behind Colour Filters / Night Light.
/// It transforms the pixels Windows is already compositing, so nothing is injected
/// into Roblox and the game process is never touched. The effect covers every
/// display while it's on, so callers apply it only while Roblox is in front and
/// call <see cref="Reset"/> otherwise.
/// </summary>
public sealed class ScreenGrade : IDisposable
{
    private readonly bool _init;
    private bool _active;
    private float[]? _last;

    public ScreenGrade()
    {
        try { _init = MagInitialize(); } catch { _init = false; }
    }

    /// <summary>Whether the API is available on this machine.</summary>
    public bool Available => _init;

    /// <summary>Sets the full-screen colour matrix (a <c>float[25]</c>, row-major).</summary>
    public void Apply(float[] matrix)
    {
        if (!_init || matrix is not { Length: 25 }) return;
        if (_active && _last is { } prev && Same(prev, matrix)) return;

        var effect = new MAGCOLOREFFECT { transform = (float[])matrix.Clone() };
        try
        {
            if (MagSetFullscreenColorEffect(ref effect))
            {
                _active = true;
                _last = effect.transform;
            }
        }
        catch { /* leave the screen untouched if it fails */ }
    }

    /// <summary>Removes the grade (back to the untouched screen).</summary>
    public void Reset()
    {
        if (!_init || !_active) return;
        var effect = new MAGCOLOREFFECT { transform = Identity() };
        try { MagSetFullscreenColorEffect(ref effect); } catch { }
        _active = false;
        _last = null;
    }

    public void Dispose()
    {
        Reset();
        if (_init) { try { MagUninitialize(); } catch { } }
    }

    private static bool Same(float[] a, float[] b)
    {
        for (int i = 0; i < 25; i++)
            if (Math.Abs(a[i] - b[i]) > 0.0005f) return false;
        return true;
    }

    private static float[] Identity()
    {
        var m = new float[25];
        m[0] = m[6] = m[12] = m[18] = m[24] = 1f;
        return m;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGCOLOREFFECT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] transform;
    }

    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT effect);
}
