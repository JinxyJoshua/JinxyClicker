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
        RefreshLicense();
    }

    // Shows the Pro page and refreshes its status, in case a pass expired since
    // the page was last seen.
    private void NavPro_Click(object sender, RoutedEventArgs e)
    {
        RefreshLicense();
        ShowPage(NavPro, PagePro, "Pro", "Unlock the paid features with a key");
    }

    // Evaluates the stored key, updates IsPro and the status line. The first time a
    // timed pass is seen it stamps the first-use date so the countdown starts now;
    // that stamp is persisted by the next SaveAppSettings (always after activation).
    private void RefreshLicense()
    {
        if (_license is null) return;

        LicenseStatus status = _license.Evaluate(_licenseKey, _licenseFirstUseUtc);
        if (status.NeedsFirstUseStamp)
            _licenseFirstUseUtc = DateTime.UtcNow;

        IsPro = status.IsPro;

        if (ProStatusText is null) return; // page not built yet
        ProStatusText.Text = status switch
        {
            { IsPro: true, Tier: ProTier.Lifetime } => "Pro — lifetime",
            { IsPro: true, ExpiresUtc: { } exp } => $"Pro — {status.Tier}, until {exp.ToLocalTime():d}",
            _ => "Free version"
        };
    }

    private void ProPaste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                ProKeyInput.Text = System.Windows.Clipboard.GetText();
        }
        catch
        {
            // Clipboard can be momentarily locked by another app; just ignore it
            // and let the user paste by hand.
        }
    }

    private void ProActivate_Click(object sender, RoutedEventArgs e)
    {
        if (_license is null) return;

        string pasted = ProKeyInput.Text ?? "";

        // PrepareActivation verifies the key and decides the first-use date:
        // a new key starts a fresh clock, the SAME key re-pasted keeps its clock,
        // so an expired pass cannot be renewed just by pasting it again.
        var prep = _license.PrepareActivation(_licenseKey, _licenseFirstUseUtc, pasted);
        if (prep is null)
        {
            ProActivateResult.Text = "That key isn't valid. Make sure you pasted the whole key.";
            return;
        }

        _licenseKey = prep.Value.Key;
        _licenseFirstUseUtc = prep.Value.FirstUseUtc;
        RefreshLicense();   // stamps a fresh pass if needed
        SaveAppSettings();  // persists the key and first-use date

        ProActivateResult.Text = IsPro
            ? "Activated. Thank you!"
            : "That key has expired.";
    }
}
