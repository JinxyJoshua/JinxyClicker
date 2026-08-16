using System;
using System.Diagnostics;

namespace JinxyClicker;

/// <summary>
/// Chooses how the screen is captured and encoded.
/// </summary>
/// <remarks>
/// Recording used to cost about two cores. Measured over an eight second
/// capture on this machine:
///
///   gdigrab + libx264   14.91s CPU
///   gdigrab + h264_amf   7.20s CPU
///   ddagrab + h264_amf   2.64s CPU
///
/// Both halves matter. GDI copies every frame through system memory and forces
/// the cursor to be redrawn — the same thing that made the pointer flicker
/// whenever the app was open — while Desktop Duplication hands over a surface
/// the GPU already has. And libx264 encodes on the CPU the game wants.
///
/// Everything here degrades to the old pipeline rather than failing. A machine
/// with no usable hardware encoder, or a driver that will not initialise one,
/// records exactly as it did before.
/// </remarks>
public static class CaptureBackend
{
    private static string? _encoderArgs;
    private static string? _encoderName;

    /// <summary>Encoder chosen for this machine, for display. Null until probed.</summary>
    public static string? EncoderName => _encoderName;

    /// <summary>
    /// Encoder arguments, probed once and cached.
    /// </summary>
    /// <remarks>
    /// The vendor decides which encoder to try — AMF is AMD's, NVENC is
    /// NVIDIA's, QSV is Intel's, and offering a card an encoder built for
    /// another vendor just fails slowly. Being listed by ffmpeg is not enough
    /// either: the encoder is compiled in regardless of what hardware is
    /// present, so it is confirmed by actually encoding a frame.
    /// </remarks>
    public static string EncoderArgs(string ffmpeg)
    {
        if (_encoderArgs != null) return _encoderArgs;

        string? adapter = GpuInfo.AdapterName();

        string? candidate =
            adapter == null ? null
            : adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "h264_nvenc"
            : adapter.Contains("AMD", StringComparison.OrdinalIgnoreCase)
              || adapter.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "h264_amf"
            : adapter.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "h264_qsv"
            : null;

        if (candidate != null && CanEncode(ffmpeg, candidate))
        {
            _encoderName = candidate;
            // Bitrate rather than CRF: hardware encoders either ignore CRF or
            // interpret it differently between vendors, and 8 Mbit is ample for
            // a desktop capture at 1080p.
            _encoderArgs = $"-c:v {candidate} -b:v 8M -pix_fmt yuv420p";
        }
        else
        {
            _encoderName = "libx264";
            _encoderArgs = "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p";
        }

        return _encoderArgs;
    }

    /// <summary>
    /// Whether the encoder actually initialises, tested by encoding one frame of
    /// a generated source. Cheap — a few hundred milliseconds, once per run.
    /// </summary>
    private static bool CanEncode(string ffmpeg, string encoder)
    {
        try
        {
            var info = new ProcessStartInfo(ffmpeg,
                $"-hide_banner -loglevel error -f lavfi -i color=black:s=256x256 " +
                $"-frames:v 1 -c:v {encoder} -f null -")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? probe = Process.Start(info);
            if (probe == null) return false;

            // Bounded: a driver that hangs on initialise must not hang the app.
            if (!probe.WaitForExit(8000))
            {
                try { probe.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return probe.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Input arguments for a capture, up to and including the source.
    /// </summary>
    /// <remarks>
    /// Desktop Duplication addresses a monitor by its DXGI output index, and
    /// cannot capture the virtual desktop spanning several of them. So a chosen
    /// display goes through ddagrab, and "all displays" stays on gdigrab —
    /// which still gains the hardware encoder, just not the cheaper capture.
    ///
    /// hwdownload is deliberate. Handing ddagrab's frames straight to AMF fails
    /// outright: it will not take the BGRA surface Desktop Duplication produces.
    /// Even paying for the download, the pipeline measured 2.64s against
    /// gdigrab's 14.91s.
    /// </remarks>
    public static string InputArgs(DisplayInfo? display, int framesPerSecond)
    {
        if (display == null)
            return $"-f gdigrab -framerate {framesPerSecond} -i desktop";

        return "-filter_complex " +
               $"\"ddagrab=output_idx={display.OutputIndex}:framerate={framesPerSecond}," +
               "hwdownload,format=bgra\"";
    }
}
