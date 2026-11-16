using System.Text.Json;

namespace GreenshotUpdater;

/// <summary>
/// Dépôt GitHub suivi : adresse de l'API <c>releases/latest</c> et fichier attendu
/// parmi les fichiers publiés de la release.
/// </summary>
public sealed record ReleaseFeed(string ApiUrl, Func<string, bool> IsAsset)
{
    /// <summary>Greenshot : installeur <c>Greenshot-INSTALLER-*-RELEASE.exe</c>.</summary>
    public static readonly ReleaseFeed Greenshot = new(
        "https://api.github.com/repos/greenshot/greenshot/releases/latest",
        name => name.StartsWith("Greenshot-INSTALLER-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith("-RELEASE.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>GreenshotUpdater lui-même : lanceur <c>GreenshotUpdater.exe</c>.</summary>
    public static readonly ReleaseFeed Self = new(
        "https://api.github.com/repos/jeanbrowaeyspro/GreenshotUpdater/releases/latest",
        name => name.Equals(AppPaths.LauncherFileName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Dernière version stable publiée sur GitHub.</summary>
/// <param name="AssetName">Nom du fichier attendu par le <see cref="ReleaseFeed"/>, s'il est publié.</param>
/// <param name="AssetUrl">Adresse de téléchargement de ce fichier.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string? AssetName, string? AssetUrl)
{
    /// <summary>
    /// Lit la réponse de l'API GitHub <c>releases/latest</c>. Renvoie null pour une
    /// préversion (drapeau <c>prerelease</c> ou nom contenant <c>UNSTABLE</c>), un
    /// brouillon ou un numéro illisible.
    /// </summary>
    public static ReleaseInfo? Parse(string json, ReleaseFeed feed)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = String(root, "tag_name") ?? "";
        var name = String(root, "name") ?? "";
        if (Flag(root, "prerelease") || Flag(root, "draft")
            || tag.Contains("UNSTABLE", StringComparison.OrdinalIgnoreCase)
            || name.Contains("UNSTABLE", StringComparison.OrdinalIgnoreCase))
            return null;
        var version = VersionText.Parse(tag);
        if (version is null)
            return null;

        string? assetName = null, assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var candidate = String(asset, "name") ?? "";
                if (feed.IsAsset(candidate))
                {
                    assetName = candidate;
                    assetUrl = String(asset, "browser_download_url");
                    break;
                }
            }
        }
        return new ReleaseInfo(version, tag, assetName, assetUrl);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>Source des versions publiées et des téléchargements.</summary>
public interface IReleaseSource
{
    /// <summary>Dernière version stable ; lève une exception si le réseau ne répond pas.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default);

    Task DownloadAsync(string url, string destination, CancellationToken cancel = default);
}

/// <summary>Accès à l'API GitHub d'un dépôt, avec l'en-tête User-Agent qu'elle exige.</summary>
public sealed class GitHubReleaseSource(ReleaseFeed feed) : IReleaseSource
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var version = typeof(GitHubReleaseSource).Assembly.GetName().Version!;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.AppName}/{version.ToString(3)}");
        return client;
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.ApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(cancel), feed);
    }

    public async Task DownloadAsync(string url, string destination, CancellationToken cancel = default)
    {
        // Délai propre au téléchargement : l'installeur pèse quelques Mo.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file, timeout.Token);
    }
}
