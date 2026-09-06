using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>A release on GitHub, reduced to the two things that matter.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string InstallerUrl, string Notes);

/// <summary>
/// Deciding whether a newer build exists, and where to get it.
/// </summary>
/// <remarks>
/// The download itself is a network call and a process launch, which cannot be
/// tested without both. Everything deciding <em>whether</em> to update and
/// <em>what</em> to fetch is parsing and comparison, and that is where this goes
/// wrong quietly — a tag that will not parse reads as "no update forever", and
/// an asset picked by position rather than by name eventually downloads
/// something that is not the installer.
/// </remarks>
public static class Updater
{
    /// <summary>The only repository releases are accepted from.</summary>
    public const string Owner = "JinxyJoshua";
    public const string Repo = "JinxyClicker";

    /// <summary>Where this build looks — public releases unless told otherwise.</summary>
    public static string LatestReleaseUrl => UpdateSource.Current.LatestReleaseUrl;

    /// <summary>
    /// Hosts a download may come from.
    /// </summary>
    /// <remarks>
    /// The installer URL arrives in a JSON response, and this app runs whatever
    /// it points at. Pinning the host means a response that has been tampered
    /// with — or a repository that changes hands — cannot redirect the update
    /// to somewhere else entirely.
    /// </remarks>
    public static bool IsTrustedDownload(string? url) =>
        UpdateSource.Current.IsTrustedDownload(url);

    /// <summary>
    /// Reads a release tag into a version.
    /// </summary>
    /// <remarks>
    /// Tags are written by hand at release time, so "v1.2.0" and "1.2.0" both
    /// happen and both have to work. Anything else returns null and is treated
    /// as no update rather than as an update to an unknown version.
    /// </remarks>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string trimmed = tag.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        return Version.TryParse(trimmed, out Version? version) ? version : null;
    }

    /// <summary>
    /// Whether a release is worth offering.
    /// </summary>
    /// <remarks>
    /// Strictly newer. Equal is the ordinary case — the version someone is
    /// already running — and offering it would prompt on every launch forever.
    /// </remarks>
    public static bool IsNewer(Version? current, Version? candidate) =>
        current != null && candidate != null && candidate > current;

    /// <summary>
    /// Picks the installer out of a release's assets.
    /// </summary>
    /// <remarks>
    /// By name, never by position. A release carries whatever was uploaded to
    /// it — source archives are added by GitHub itself — and taking the first
    /// asset would eventually hand a zip to a routine that runs executables.
    /// </remarks>
    public static string? PickInstaller(IEnumerable<(string Name, string Url)> assets) =>
        assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase)
                && IsTrustedDownload(a.Url))
            .Url;

    /// <summary>
    /// Reads GitHub's release JSON into the parts this app uses.
    /// </summary>
    /// <returns>Null when the response carries no usable release.</returns>
    public static ReleaseInfo? ReadRelease(string json)
    {
        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            // Drafts are not published and prereleases are opt-in; neither is
            // something to push at everyone who opens the app.
            if (root.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
                return null;
            if (root.TryGetProperty("prerelease", out JsonElement pre) && pre.GetBoolean())
                return null;

            string? tag = root.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() : null;
            Version? version = ParseVersion(tag);
            if (version == null) return null;

            var assets = new List<(string, string)>();

            if (root.TryGetProperty("assets", out JsonElement list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in list.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                    // Which field to read depends on the source: a private
                    // release's browser_download_url serves a login page to
                    // anyone without a session, and downloading it would
                    // install an HTML page renamed to .exe.
                    string? url = asset.TryGetProperty(
                        UpdateSource.Current.AssetUrlField, out JsonElement u)
                        ? u.GetString() : null;

                    if (name != null && url != null) assets.Add((name, url));
                }
            }

            string? installer = PickInstaller(assets);
            if (installer == null) return null;

            string notes = root.TryGetProperty("body", out JsonElement b)
                ? b.GetString() ?? "" : "";

            return new ReleaseInfo(version, tag!.Trim(), installer, notes);
        }
        catch
        {
            // A malformed or unexpected response is not an update. Failing quiet
            // is right here: this runs at startup and nobody asked for it.
            return null;
        }
    }

    /// <summary>
    /// Trims release notes to something that fits in a prompt.
    /// </summary>
    public static string Summarise(string notes, int maxLines = 6, int maxChars = 400)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";

        string[] lines = notes
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(PlainText)
            .Where(l => l.Length > 0)
            .Take(maxLines)
            .ToArray();

        string joined = string.Join(Environment.NewLine, lines);

        return joined.Length <= maxChars ? joined : Clip(joined, maxChars) + "…";
    }

    /// <summary>
    /// One line of Markdown as the plain text a message box can show.
    /// </summary>
    /// <remarks>
    /// Release notes are written for GitHub, which renders them. The update
    /// prompt is a MessageBox, which does not — so the 1.4.6 prompt showed
    /// people a literal "## Mouse buttons can be bound again" and
    /// '**"Side buttons only"**'. Punctuation meant to disappear, left in.
    ///
    /// Deliberately small. Headings, emphasis, code, links, rules and bullets
    /// are what these notes actually use; this is not a Markdown parser and
    /// should not become one. Anything it does not recognise it leaves alone,
    /// which is the right failure for text that is only being displayed.
    /// </remarks>
    private static string PlainText(string line)
    {
        string text = line.Trim();

        // A rule carries nothing once it cannot be drawn.
        if (RuleLine.IsMatch(text)) return "";

        text = Heading.Replace(text, "");
        text = Quote.Replace(text, "");

        // Before emphasis, so a link's text keeps any emphasis inside it.
        text = Link.Replace(text, "$1");

        text = Bold.Replace(text, "$2");
        text = Italic.Replace(text, "$2");
        text = Code.Replace(text, "$1");

        // Bullets keep their shape. A list read as running prose is worse than
        // a list with a character in front of it.
        text = Bullet.Replace(text, "• ");

        return text.Trim();
    }

    private static readonly Regex RuleLine = new(@"^\s*([-*_])\s*(\1\s*){2,}$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}\s*", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^>\s*", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"(\*\*|__)(.+?)\1", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![*_\w])([*_])(?!\s)(.+?)(?<!\s)\1(?![*_\w])", RegexOptions.Compiled);
    private static readonly Regex Code = new(@"`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^[-*+]\s+", RegexOptions.Compiled);

    /// <summary>
    /// Cuts to a length without cutting through a word.
    /// </summary>
    /// <remarks>
    /// The 1.4.6 prompt ended "and it is left unless…", stopping inside the
    /// sentence and in the middle of nothing in particular. Backing up to the
    /// last space costs a few characters and ends on a whole word.
    /// </remarks>
    private static string Clip(string text, int maxChars)
    {
        string cut = text[..maxChars];

        int space = cut.LastIndexOfAny(new[] { ' ', '\n', '\r' });

        // Only when a reasonable amount survives. A single very long word would
        // otherwise back up to almost nothing.
        if (space > maxChars / 2) cut = cut[..space];

        return cut.TrimEnd();
    }
}
