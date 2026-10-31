using System;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// Plays the short sound that confirms a clip was saved. The three built-in sounds
/// are generated once to temp WAV files (so nothing has to ship with the app) and
/// played through a <see cref="MediaPlayer"/>, which also gives a volume control
/// and lets the player point at their own MP3/M4A/WAV instead.
/// </summary>
public sealed class ClipSound
{
    private const int Rate = 44100;

    private readonly MediaPlayer _player = new();
    private readonly string _dir;

    public ClipSound()
    {
        _dir = Path.Combine(Path.GetTempPath(), "JinxyClipSounds");
        try { Directory.CreateDirectory(_dir); } catch { }
    }

    /// <summary>Plays the chosen sound (or the player's own file) at 0‑1 volume.</summary>
    public void Play(string choice, string? ownPath, double volume01)
    {
        try
        {
            string? path = choice == "own"
                ? (string.IsNullOrWhiteSpace(ownPath) ? null : ownPath)
                : BuiltinPath(choice);
            if (path == null || !File.Exists(path)) return;

            _player.Volume = Math.Clamp(volume01, 0, 1);
            _player.Stop();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch { /* a clip is saved whether or not the chime plays */ }
    }

    private string? BuiltinPath(string choice)
    {
        string name = choice is "pop" or "shutter" ? choice : "chime";
        string path = Path.Combine(_dir, name + ".wav");
        try
        {
            if (!File.Exists(path)) File.WriteAllBytes(path, Wav(Samples(name)));
        }
        catch { return null; }
        return path;
    }

    private static float[] Samples(string name) => name switch
    {
        "pop" => Tone(1000, 0.07, 0, decay: true),
        "shutter" => Shutter(),
        _ => Mix(Tone(880, 0.16, 0), Tone(1318.5, 0.26, 0.10))   // two gentle bells
    };

    private static float[] Tone(double freq, double seconds, double startSeconds, bool decay = false)
    {
        int start = (int)(startSeconds * Rate);
        int len = (int)(seconds * Rate);
        var s = new float[start + len];
        for (int i = 0; i < len; i++)
        {
            double t = i / (double)Rate;
            double env = decay ? Math.Exp(-t / (seconds * 0.4)) : Envelope(i, len);
            s[start + i] = (float)(Math.Sin(2 * Math.PI * freq * t) * 0.6 * env);
        }
        return s;
    }

    private static float[] Shutter()
    {
        int n = (int)(Rate * 0.12);
        var s = new float[n];
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double env = Math.Exp(-i / (Rate * 0.03));
            s[i] = (float)((rng.NextDouble() * 2 - 1) * 0.5 * env);
        }
        return s;
    }

    private static double Envelope(int i, int len)
    {
        int fade = Math.Min(len / 4, (int)(Rate * 0.02));
        if (fade <= 0) return 1;
        if (i < fade) return i / (double)fade;
        if (i > len - fade) return (len - i) / (double)fade;
        return 1.0;
    }

    private static float[] Mix(float[] a, float[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double v = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            s[i] = (float)Math.Clamp(v, -1, 1);
        }
        return s;
    }

    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Length * 2;

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(Rate);
        w.Write(Rate * 2);          // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        foreach (float f in samples)
            w.Write((short)(Math.Clamp(f, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
