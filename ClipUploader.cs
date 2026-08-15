using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

public readonly record struct UploadResult(bool Success, string Message, string? Url);

/// <summary>
/// Turns a local clip into a shareable link.
/// </summary>
/// <remarks>
/// Catbox was chosen because it is the only option that satisfies all three of
/// the stated requirements at once: works on any PC, needs no account or OAuth,
/// and hands back a usable URL. The alternatives each fail one of those —
/// Medal's API only triggers its own installed client, and YouTube locks
/// uploads from unaudited API projects to private, which has no shareable link.
///
/// The trade is that the link is public to anyone holding it, and there is no
/// reliable delete. Callers must treat uploading as an explicit user action,
/// never something that happens automatically after a recording.
/// </remarks>
public static class ClipUploader
{
    private const string Endpoint = "https://catbox.moe/user/api.php";

    /// <summary>Catbox's documented ceiling. Checked locally to fail fast rather than after a long upload.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task<UploadResult> UploadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            return new UploadResult(false, "That file no longer exists.", null);

        var file = new FileInfo(path);

        if (file.Length == 0)
            return new UploadResult(false, "That file is empty.", null);

        if (file.Length > MaxBytes)
        {
            return new UploadResult(false,
                $"That file is {file.Length / 1024.0 / 1024.0:0} MB — the limit is {MaxBytes / 1024 / 1024} MB.", null);
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent("fileupload"), "reqtype" }
            };

            await using FileStream stream = File.OpenRead(path);
            using var content = new StreamContent(stream);
            form.Add(content, "fileToUpload", file.Name);

            // No userhash: anonymous upload, so nothing is tied to an account.
            using HttpResponseMessage response =
                await Client.PostAsync(Endpoint, form, token).ConfigureAwait(false);

            string body = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim();

            if (!response.IsSuccessStatusCode)
                return new UploadResult(false, $"Upload failed ({(int)response.StatusCode}). {Truncate(body)}", null);

            // Success is the bare URL in the body; errors come back as prose
            // with a 200, so the shape of the response is what has to be checked.
            return Uri.TryCreate(body, UriKind.Absolute, out Uri? uri)
                   && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? new UploadResult(true, "Uploaded.", body)
                : new UploadResult(false, $"The host rejected it: {Truncate(body)}", null);
        }
        catch (OperationCanceledException)
        {
            return new UploadResult(false, "Upload cancelled.", null);
        }
        catch (Exception ex)
        {
            return new UploadResult(false, $"Upload failed: {ex.Message}", null);
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
