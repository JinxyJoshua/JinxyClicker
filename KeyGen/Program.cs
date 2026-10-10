using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JinxyClicker;

// A tiny window for minting keys: click the tier someone paid for, the key is
// shown and copied to the clipboard, and logged. This is the private seller tool
// and never ships with the app.
internal static class Program
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JinxyKeyGen");
    private static readonly string KeyFile = Path.Combine(Dir, "signing-key.txt");
    private static readonly string LogFile = Path.Combine(Dir, "keys.csv");

    private static TextBox _output = null!;
    private static TextBlock _status = null!;

    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Run(BuildWindow());
    }

    private static Window BuildWindow()
    {
        var panel = new StackPanel { Margin = new Thickness(18) };

        panel.Children.Add(new TextBlock
        {
            Text = "Jinxy KeyGen",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Click the tier the buyer paid for. The key appears below and is copied to your clipboard automatically — just paste it to them.",
            Foreground = Brushes.Silver,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14)
        });

        var row = new UniformGrid { Columns = 4, Height = 56 };
        row.Children.Add(TierButton("Lifetime\n$20", ProTier.Lifetime));
        row.Children.Add(TierButton("Month\n$8", ProTier.Month));
        row.Children.Add(TierButton("Weekend\n$4", ProTier.Weekend));
        row.Children.Add(TierButton("Day\n$2", ProTier.Day));
        panel.Children.Add(row);

        _output = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Margin = new Thickness(0, 14, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        panel.Children.Add(_output);

        var copyBtn = new Button
        {
            Content = "Copy key again",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_output.Text))
            {
                TrySetClipboard(_output.Text);
                SetStatus("Copied to clipboard.");
            }
        };
        panel.Children.Add(copyBtn);

        _status = new TextBlock
        {
            Foreground = Brushes.Silver,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_status);

        return new Window
        {
            Title = "Jinxy KeyGen",
            Width = 560,
            Height = 350,
            Background = (Brush)new BrushConverter().ConvertFrom("#15161B")!,
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
    }

    private static Button TierButton(string label, ProTier tier)
    {
        var b = new Button { Content = label, Margin = new Thickness(4), FontWeight = FontWeights.Bold };
        b.Click += (_, _) => Generate(tier);
        return b;
    }

    private static void Generate(ProTier tier)
    {
        try
        {
            if (!File.Exists(KeyFile))
            {
                SetStatus("No signing key found at " + KeyFile + "\nRestore it from your backup before making keys.");
                return;
            }

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            key.ImportECPrivateKey(Convert.FromBase64String(File.ReadAllText(KeyFile)), out _);

            ulong id = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            string keyString = LicenseKeyCodec.Create(new LicensePayload(tier, id), key);

            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogFile, $"{DateTime.UtcNow:o},{tier},{id},{keyString}\n");

            _output.Text = keyString;
            TrySetClipboard(keyString);
            SetStatus($"{tier} key made and copied. Send it to the buyer. (Logged to keys.csv)");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard busy; the key is still shown to copy by hand */ }
    }

    private static void SetStatus(string s) => _status.Text = s;
}
