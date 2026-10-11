using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// Sends feedback to the Worker and (dev build only) reads the inbox back. The
/// read and delete calls carry a bearer token in the header — never in the URL —
/// so the dev secret stays out of logs and history. All the parsing lives in
/// <see cref="Feedback"/>; this is just the HTTP.
/// </summary>
public static class FeedbackClient
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "JinxyClicker/1.0 (+https://github.com/JinxyJoshua/JinxyClicker)");
        return c;
    }

    /// <summary>Validates, then posts one submission. Safe to call from any build.</summary>
    public static async Task<FeedbackResult> SubmitAsync(FeedbackDraft draft, string version, CancellationToken token)
    {
        string? err = Feedback.Validate(draft.Summary, draft.Details);
        if (err != null) return new FeedbackResult(false, err);
        if (string.IsNullOrWhiteSpace(Feedback.ServerUrl))
            return new FeedbackResult(false, "Sending isn't set up yet — try again after the next update.");

        try
        {
            string json = Feedback.BuildSubmitJson(draft, version);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage res =
                await Client.PostAsync($"{Feedback.ServerUrl}/submit", content, token).ConfigureAwait(false);
            string body = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return Feedback.ParseSubmitResponse((int)res.StatusCode, body);
        }
        catch (OperationCanceledException)
        {
            return new FeedbackResult(false, "Cancelled.");
        }
        catch (Exception ex)
        {
            return new FeedbackResult(false, $"Couldn't send that — check your connection. ({Short(ex)})");
        }
    }

    /// <summary>Dev build: reads the inbox. The token gates the Worker's read route.</summary>
    public static async Task<(bool Ok, string? Error, IReadOnlyList<FeedbackItem> Items)> ListAsync(
        string devToken, CancellationToken token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Feedback.ServerUrl}/list");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
            using HttpResponseMessage res = await Client.SendAsync(req, token).ConfigureAwait(false);
            string body = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return (false, $"Inbox fetch failed ({(int)res.StatusCode}).", Array.Empty<FeedbackItem>());
            return (true, null, Feedback.ParseList(body));
        }
        catch (Exception ex)
        {
            return (false, Short(ex), Array.Empty<FeedbackItem>());
        }
    }

    /// <summary>Dev build: deletes one submission by id.</summary>
    public static async Task<(bool Ok, string? Error)> DeleteAsync(string devToken, string id, CancellationToken token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Feedback.ServerUrl}/delete");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
            string json = JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id });
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage res = await Client.SendAsync(req, token).ConfigureAwait(false);
            return res.IsSuccessStatusCode ? (true, null) : (false, $"Delete failed ({(int)res.StatusCode}).");
        }
        catch (Exception ex)
        {
            return (false, Short(ex));
        }
    }

    private static string Short(Exception ex) =>
        ex.Message.Length <= 160 ? ex.Message : ex.Message[..160] + "…";
}
