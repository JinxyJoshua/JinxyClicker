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
    /// <summary>
    /// Bits per pixel per frame, which is what a bitrate has to be derived from.
    /// </summary>
    /// <remarks>
    /// The bitrate was a flat 8 Mbit for every recording. That is roughly right
    /// for 1080p30 and wrong everywhere else in the same direction: at 1080p60
    /// it is half the bits per frame, and on a 1440p or 4K machine the same 8
    /// Mbit is spread across two to four times the pixels. A real clip measured
    /// here came out at 7.8 Mbit for 1080p60 of fast motion, which is where
    /// "you can barely see what is going on" comes from — and why it looked
    /// worse on some machines than others while nothing about the setting
    /// changed.
    ///
    /// 0.09 is a middle figure for h264 at this kind of motion. Hardware
    /// encoders are less efficient than x264 at the same bitrate, so being
    /// generous costs disk and buys clarity, which is the right way round for a
    /// clip somebody is going to upload.
    /// </remarks>
    public const double BitsPerPixelPerFrame = 0.09;

    /// <summary>Never go below this, however small the capture.</summary>
    public const int MinimumMbit = 6;

    /// <summary>Or above it, however large. 4K60 would otherwise ask for 45.</summary>
    public const int MaximumMbit = 40;

    /// <summary>The bitrate for a capture of this size and rate, in megabits.</summary>
    public static int BitrateMbit(int width, int height, int fps)
    {
        if (width <= 0 || height <= 0 || fps <= 0) return MinimumMbit;

        double megabits = (double)width * height * fps * BitsPerPixelPerFrame / 1_000_000.0;

        return (int)Math.Round(Math.Clamp(megabits, MinimumMbit, MaximumMbit));
    }

    /// <summary>The bitrate for a capture of one monitor, or of the whole desktop.</summary>
    public static int BitrateMbit(DisplayInfo? display, int fps)
    {
        if (display != null) return BitrateMbit(display.EvenWidth, display.EvenHeight, fps);

        (int width, int height) = Displays.VirtualDesktopSize();

        return BitrateMbit(width, height, fps);
    }

    public static string EncoderArgs(string ffmpeg, DisplayInfo? display, int fps)
    {
        int mbit = BitrateMbit(display, fps);

        return EncoderArgs(ffmpeg).Replace("{BITRATE}", mbit.ToString());
    }

    private static string EncoderArgs(string ffmpeg)
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
            // interpret it differently between vendors. The figure is filled in
            // per capture, because it depends on how many pixels at what rate.
            _encoderArgs = $"-c:v {candidate} -b:v {{BITRATE}}M -pix_fmt yuv420p";
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
    /// Pacing for the output stream, applied to every capture.
    /// </summary>
    /// <remarks>
    /// Screen capture is inherently variable: a frame arrives when the desktop
    /// changes, and the grabber falls behind under load. Written straight out,
    /// that produces a variable frame rate file — a real capture here asked for
    /// 30 and recorded 28.12, with 16 of its 208 frames irregularly spaced.
    ///
    /// Players handle that badly, and the way they handle it badly is exactly
    /// what people describe as the clip being laggy or stuttering while the
    /// recording itself looked fine. Constant frame rate makes ffmpeg duplicate
    /// or drop to hold the requested rate, so the file plays at the speed it
    /// claims. The same capture with this set measured 0 irregular frames of
    /// 239, at a flat 30.
    /// </remarks>
    public const string PacingArgs = "-fps_mode cfr";

    /// <summary>
    /// Drops a capture process below the game in the scheduler's priorities.
    /// </summary>
    /// <remarks>
    /// Encoding is steady, heavy work that runs for as long as the game does,
    /// and at normal priority it competes with the game on equal terms for
    /// exactly the frames the game needs. Below normal, Windows hands the
    /// contested time to the game and gives the encoder the rest.
    ///
    /// It does not cost frames in the capture: measured over eight seconds the
    /// process still delivered every frame at the requested rate, with none of
    /// them irregularly spaced. Best-effort — a refusal here is not worth
    /// failing a recording over.
    /// </remarks>
    public static void YieldToTheGame(Process process)
    {
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* Already gone, or refused by policy. The recording is fine. */ }
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
