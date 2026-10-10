using System;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The Pro gate wired into the window. The app does not keep a live AppSettings
/// object — it loads one, applies it, and rebuilds a fresh one on save — so the
/// license key and its first-use date are held here as fields and written back by
/// <c>SaveAppSettings</c>. A missing or invalid key simply leaves <see cref="IsPro"/>
/// false; nothing here is trusted, because the key is re-verified every launch.
/// </summary>
public partial class MainWindow
{
    private License? _license;
    private string? _licenseKey;
    private DateTime? _licenseFirstUseUtc;

    /// <summary>True when a valid Pro key is active. Every Pro feature reads this.</summary>
    public bool IsPro { get; private set; }

    // Called from ApplyAppSettings with the freshly loaded settings.
    private void LoadLicense(AppSettings s)
    {
        _license = new License(LicenseKeys.PublicKey(), LicenseKeys.Blocklist, () => DateTime.UtcNow);
        _licenseKey = s.LicenseKey;
        _licenseFirstUseUtc = s.LicenseFirstUseUtc;
        RefreshLicense(persistStamp: true);
    }

    // Evaluates the stored key, updates IsPro and the status line. The first time a
    // timed pass is seen it stamps the first-use date so the countdown starts now.
    private void RefreshLicense(bool persistStamp)
    {
        LicenseStatus status = _license!.Evaluate(_licenseKey, _licenseFirstUseUtc);

        if (status.NeedsFirstUseStamp)
        {
            _licenseFirstUseUtc = DateTime.UtcNow;
            if (persistStamp) SaveAppSettings();
        }

        IsPro = status.IsPro;
        ProStatusText.Text = status switch
        {
            { IsPro: true, Tier: ProTier.Lifetime } => "Pro — lifetime",
            { IsPro: true, ExpiresUtc: { } e } => $"Pro — {status.Tier}, until {e.ToLocalTime():d}",
            _ => "Free version"
        };
    }

    private void ProActivate_Click(object sender, RoutedEventArgs e)
    {
        string key = ProKeyInput.Text?.Trim() ?? "";

        // A freshly pasted key starts its own clock, so judge it with no prior
        // first-use date — a leftover one from an earlier key must not apply.
        LicenseStatus probe = _license!.Evaluate(key, null);
        if (!probe.IsPro)
        {
            ProActivateResult.Text = "That key isn't valid.";
            return;
        }

        _licenseKey = key;
        _licenseFirstUseUtc = null;
        RefreshLicense(persistStamp: false);
        SaveAppSettings();
        ProActivateResult.Text = "Activated. Thank you!";
    }
}
