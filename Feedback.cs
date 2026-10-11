using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>What a suggestion is about. The three choices match the submit form's tabs.</summary>
public enum FeedbackType { Feature, Improvement, Bug }

/// <summary>What the user typed into the form, before it is sent.</summary>
public readonly record struct FeedbackDraft(FeedbackType Type, string Summary, string Details);

/// <summary>The outcome of trying to send, with a line to show the user either way.</summary>
public readonly record struct FeedbackResult(bool Success, string Message);

/// <summary>One stored submission, as the dev inbox reads them back.</summary>
public sealed record FeedbackItem(
    string Id, FeedbackType Type, string Summary, string Details, string Version, DateTimeOffset Submitted);

/// <summary>
/// Pure helpers for the feedback feature: input validation, request building and
/// response parsing. No network and no WPF, so it is all unit-testable. The
/// <see cref="FeedbackClient"/> does the actual HTTP using these.
/// </summary>
public static class Feedback
{
    /// <summary>
    /// The feedback Worker (separate from the license Worker, so a flood of
    /// submissions can never affect key-checking). Deploy it on the same account
    /// and set this to its URL. Empty disables sending.
    /// </summary>
    public const string ServerUrl = "https://jinxy-feedback.jinxy-server-globe.workers.dev";

    public const int MaxSummary = 120;
    public const int MaxDetails = 4000;

    public static string TypeSlug(FeedbackType t) => t switch
    {
        FeedbackType.Improvement => "improvement",
        FeedbackType.Bug => "bug",
        _ => "feature"
    };

    public static FeedbackType ParseType(string? slug) => slug?.Trim().ToLowerInvariant() switch
    {
        "improvement" => FeedbackType.Improvement,
        "bug" => FeedbackType.Bug,
        _ => FeedbackType.Feature
    };

    /// <summary>The form-tab label for a type.</summary>
    public static string TypeLabel(FeedbackType t) => t switch
    {
        FeedbackType.Improvement => "Improvement",
        FeedbackType.Bug => "Something's broken",
        _ => "New feature"
    };

    /// <summary>
    /// Checks the input before sending. Returns the error to show, or null when it
    /// is good to send. Details are optional; a one-line summary is the minimum.
    /// </summary>
    public static string? Validate(string? summary, string? details)
    {
        string s = (summary ?? string.Empty).Trim();
        if (s.Length == 0) return "Add a one-line summary first.";
        if (s.Length > MaxSummary) return $"Keep the summary under {MaxSummary} characters.";
        if ((details ?? string.Empty).Length > MaxDetails)
            return $"Details can be at most {MaxDetails} characters.";
        return null;
    }

    /// <summary>Builds the JSON body the Worker expects, trimming the text fields.</summary>
    public static string BuildSubmitJson(FeedbackDraft draft, string version)
    {
        var payload = new Dictionary<string, string>
        {
            ["type"] = TypeSlug(draft.Type),
            ["summary"] = (draft.Summary ?? string.Empty).Trim(),
            ["details"] = (draft.Details ?? string.Empty).Trim(),
            ["version"] = (version ?? string.Empty).Trim()
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Turns the Worker's submit response into a user-facing result.</summary>
    public static FeedbackResult ParseSubmitResponse(int statusCode, string? body)
    {
        const string sent = "Sent — thanks. Your suggestion is in.";

        if (statusCode == 429)
            return new FeedbackResult(false, "You're sending these a little fast — give it a minute and try again.");

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                        return new FeedbackResult(true, sent);
                    if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                        return new FeedbackResult(false, err.GetString() ?? "That didn't go through.");
                }
            }
            catch (JsonException) { /* fall through to the status-code verdict */ }
        }

        if (statusCode is >= 200 and < 300)
            return new FeedbackResult(true, sent);

        return new FeedbackResult(false, $"Couldn't send that ({statusCode}). Check your connection and try again.");
    }

    /// <summary>
    /// Parses the dev inbox list. Accepts either a bare array or an object with an
    /// "items" array. Always returns newest-first; malformed input yields empty.
    /// </summary>
    public static IReadOnlyList<FeedbackItem> ParseList(string? body)
    {
        var items = new List<FeedbackItem>();
        if (string.IsNullOrWhiteSpace(body)) return items;

        try
        {
            using var doc = JsonDocument.Parse(body);
            JsonElement arr = doc.RootElement;
            if (arr.ValueKind == JsonValueKind.Object && arr.TryGetProperty("items", out var inner))
                arr = inner;
            if (arr.ValueKind != JsonValueKind.Array) return items;

            foreach (JsonElement el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string id = GetStr(el, "id");
                string summary = GetStr(el, "summary");
                if (id.Length == 0 && summary.Length == 0) continue;
                items.Add(new FeedbackItem(
                    id,
                    ParseType(GetStr(el, "type")),
                    summary,
                    GetStr(el, "details"),
                    GetStr(el, "version"),
                    ParseTs(el)));
            }
        }
        catch (JsonException)
        {
            return new List<FeedbackItem>();
        }

        return items.OrderByDescending(i => i.Submitted).ToList();
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static DateTimeOffset ParseTs(JsonElement el)
    {
        if (el.TryGetProperty("ts", out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long ms))
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), out var dto))
                return dto;
        }
        return DateTimeOffset.MinValue;
    }
}
