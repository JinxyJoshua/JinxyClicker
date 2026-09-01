using System;

namespace JinxyClicker;

/// <summary>
/// Where this build looks for updates.
/// </summary>
/// <remarks>
/// There are two builds now, and without this they would share one update
/// path — which is worse than it sounds. The public installer contains no
/// developer panel, so a dev build that updated from the public release would
/// download it, run it, and replace itself with the public app. The dev tab
/// would vanish and look like a bug, when in fact the build had quietly
/// overwritten itself with a different product.
///
/// So a dev build points somewhere else, or nowhere. Nowhere is the default and
/// is safe: it means the dev build never updates itself, and is kept current by
/// rebuilding from source, which is what run-dev.cmd does.
///
/// <b>Making dev builds update themselves</b> means putting the dev installer
/// somewhere the public cannot reach — a private repository — and giving this
/// build a token to read it. Be clear-eyed about what that token is: it ships
/// inside every dev build you hand out, so it is exactly as private as the
/// people you hand them to. It is not a security boundary against someone who
/// already has a dev build; it stops the installer being found by anyone who
/// does not. Make it fine-grained, read-only, and scoped to that one
/// repository, so the worst case is that the dev installer leaks — which is
/// the same thing that happens if a dev build leaks anyway.
/// </remarks>
public sealed record UpdateSource(string Owner, string Repo, string Token = "")
{
    /// <summary>The public releases everybody updates from.</summary>
    public static readonly UpdateSource Public = new("JinxyJoshua", "JinxyClicker");

    /// <summary>
    /// A build that must not update itself.
    /// </summary>
    /// <remarks>
    /// What a dev build uses until a private source is configured. Silence is
    /// the right behaviour here — the alternative is replacing itself with the
    /// public app.
    /// </remarks>
    public static readonly UpdateSource None = new("", "");

    /// <summary>Where this build actually looks. Set once, at startup.</summary>
    public static UpdateSource Current { get; set; } = Public;

    public bool CanUpdate => Owner.Length > 0 && Repo.Length > 0;

    /// <summary>Whether the releases need a token to read.</summary>
    public bool IsPrivate => Token.Length > 0;

    public string LatestReleaseUrl =>
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    /// <summary>
    /// Which field of an asset holds the address to download from.
    /// </summary>
    /// <remarks>
    /// A public asset is fetched from browser_download_url. A private one
    /// cannot be — that address returns a login page to anyone without a
    /// session — and has to come from the API's own "url" field, requested with
    /// the token and an octet-stream Accept header. Picking the wrong one is
    /// not an error anybody sees: the download succeeds and installs an HTML
    /// page renamed to .exe.
    /// </remarks>
    public string AssetUrlField => IsPrivate ? "url" : "browser_download_url";

    /// <summary>Whether a download address is one this source may use.</summary>
    /// <remarks>
    /// A private source adds api.github.com, because that is where its assets
    /// live. It is deliberately not trusted for a public source: the public
    /// path has no reason to download from the API, and a narrower list is a
    /// narrower thing to get wrong.
    /// </remarks>
    public bool IsTrustedDownload(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || (IsPrivate && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase));
    }
}
