using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexTaskMonitor.Core;

public sealed record PetdexCatalogEntry(
    string Slug,
    string DisplayName,
    string Description,
    string? Kind,
    string? SubmittedBy,
    Uri SourcePageUri,
    Uri PetJsonUri,
    Uri SpriteSheetUri,
    IReadOnlyList<string> Actions);

public sealed record PetdexDownloadedPackage(DownloadedPetPackage Package, byte[] SpriteSheetBytes);

public sealed class PetdexCatalogClient
{
    public static readonly Uri ManifestUri = new("https://petdex.dev/api/manifest");
    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int MaxPackageBytes = 128 * 1024;
    private const int MaxSpriteSheetBytes = 12 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private static readonly Regex ManifestPetPattern = new(
        "\\\"slug\\\"\\s*:\\s*\\\"(?<slug>[^\\\"]+)\\\"(?<content>.*?)\\\"spritesheetUrl\\\"\\s*:\\s*\\\"(?<sprite>https://[^\\\"]+)\\\"\\s*,\\s*\\\"petJsonUrl\\\"\\s*:\\s*\\\"(?<package>https://[^\\\"]+)\\\"",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public PetdexCatalogClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<IReadOnlyList<PetdexCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await DownloadCatalogTextAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(manifest);
            if (!document.RootElement.TryGetProperty("pets", out var pets) || pets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Petdex returned an invalid catalog.");
            }

            var entries = new List<PetdexCatalogEntry>();
            foreach (var pet in pets.EnumerateArray())
            {
                var entry = ParseEntry(pet);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return Sort(entries);
        }
        catch (JsonException)
        {
            var entries = new List<PetdexCatalogEntry>();
            foreach (Match match in ManifestPetPattern.Matches(manifest))
            {
                var entry = ParseTolerantEntry(match);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            if (entries.Count == 0)
            {
                throw new InvalidDataException("Petdex returned an unreadable catalog.");
            }

            return Sort(entries);
        }
    }

    public async Task<DownloadedPetPackage> DownloadPackageAsync(
        PetdexCatalogEntry entry,
        CancellationToken cancellationToken = default) =>
        (await DownloadAsync(entry, cancellationToken)).Package;

    public async Task<PetdexDownloadedPackage> DownloadAsync(
        PetdexCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        EnsureHttps(entry.PetJsonUri);
        EnsureHttps(entry.SpriteSheetUri);
        var packageJsonTask = DownloadTextAsync(entry.PetJsonUri, MaxPackageBytes, cancellationToken);
        var spriteSheetTask = DownloadBytesAsync(entry.SpriteSheetUri, MaxSpriteSheetBytes, cancellationToken);
        await Task.WhenAll(packageJsonTask, spriteSheetTask);

        var packageJson = await packageJsonTask;
        var metadata = ParsePackageMetadata(packageJson, entry);
        var package = new DownloadedPetPackage(
            entry.Slug,
            metadata.DisplayName,
            metadata.Description,
            entry.SourcePageUri.AbsoluteUri,
            entry.PetJsonUri.AbsoluteUri,
            entry.SpriteSheetUri.AbsoluteUri,
            entry.Actions.Count > 0 ? entry.Actions : StandardActions,
            SpriteSheetLayout.Standard,
            packageJson);
        return new PetdexDownloadedPackage(package, await spriteSheetTask);
    }

    public async Task<byte[]> DownloadSpriteSheetAsync(PetdexCatalogEntry entry, CancellationToken cancellationToken = default) =>
        await DownloadBytesAsync(entry.SpriteSheetUri, MaxSpriteSheetBytes, cancellationToken);

    public Task<byte[]> DownloadPreviewSpriteAsync(Uri uri, CancellationToken cancellationToken = default) =>
        DownloadBytesAsync(uri, MaxSpriteSheetBytes, cancellationToken);

    public static string NormalizeSlugOrUrl(string value)
    {
        var trimmed = value.Trim();
        var command = Regex.Match(
            trimmed,
            "^npx(?:\\s+-y)?\\s+petdex(?:@[A-Za-z0-9._-]+)?\\s+install\\s+(?<slug>[A-Za-z0-9_-]+)\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (command.Success)
        {
            return command.Groups["slug"].Value.ToLowerInvariant();
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "petdex.dev", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Only Petdex page links can be installed here.");
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[0], "pets", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }

            throw new InvalidDataException("This Petdex link does not point to a pet.");
        }

        var slug = new string(trimmed.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(slug) || !string.Equals(slug, trimmed, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Enter a Petdex pet name or a Petdex pet page link.");
        }

        return slug.ToLowerInvariant();
    }

    private static readonly IReadOnlyList<string> StandardActions =
        ["idle", "run", "failed", "review", "wave", "waiting"];

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexTaskMonitor/1.0 (Petdex-library)");
        return client;
    }

    private async Task<string> DownloadTextAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        var bytes = await DownloadBytesAsync(uri, maximumBytes, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<string> DownloadCatalogTextAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await DownloadTextAsync(ManifestUri, MaxManifestBytes, cancellationToken);
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (error is HttpRequestException { StatusCode: { } status } &&
                    status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests) && (int)status < 500)
                {
                    throw;
                }

                if (attempt >= 1)
                {
                    if (error is OperationCanceledException)
                    {
                        throw new TimeoutException("The Petdex catalog request timed out.", error);
                    }

                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
            }
        }
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        EnsureHttps(uri);
        // ResponseHeadersRead does not apply HttpClient.Timeout while reading the body.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_httpClient.Timeout);
        var requestToken = timeout.Token;
        var currentUri = uri;
        for (var redirectCount = 0; redirectCount <= 4; redirectCount++)
        {
            using var response = await _httpClient.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    throw new InvalidDataException("Petdex returned an incomplete redirect.");
                }

                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                EnsureHttps(currentUri);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maximumBytes)
            {
                throw new InvalidDataException("The Petdex download is larger than the safety limit.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(requestToken);
            await using var destination = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, requestToken);
                if (read == 0)
                {
                    return destination.ToArray();
                }

                if (destination.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("The Petdex download is larger than the safety limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), requestToken);
            }
        }

        throw new InvalidDataException("Petdex redirected too many times.");
    }

    private static PetdexCatalogEntry? ParseEntry(JsonElement pet)
    {
        var slug = GetString(pet, "slug");
        var displayName = GetString(pet, "displayName");
        var spritesheetUrl = GetString(pet, "spritesheetUrl");
        var petJsonUrl = GetString(pet, "petJsonUrl");
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(displayName) ||
            !Uri.TryCreate(spritesheetUrl, UriKind.Absolute, out var spritesheetUri) ||
            !Uri.TryCreate(petJsonUrl, UriKind.Absolute, out var petJsonUri))
        {
            return null;
        }

        var pageUri = new Uri($"https://petdex.dev/pets/{Uri.EscapeDataString(slug)}");
        return new PetdexCatalogEntry(
            slug,
            displayName,
            GetString(pet, "description"),
            NullIfEmpty(GetString(pet, "kind")),
            NullIfEmpty(GetString(pet, "submittedBy")),
            pageUri,
            petJsonUri,
            spritesheetUri,
            GetActions(pet));
    }

    private static PetdexCatalogEntry? ParseTolerantEntry(Match match)
    {
        var slug = match.Groups["slug"].Value;
        var content = match.Groups["content"].Value;
        if (!Uri.TryCreate(match.Groups["sprite"].Value, UriKind.Absolute, out var spriteSheetUri) ||
            !Uri.TryCreate(match.Groups["package"].Value, UriKind.Absolute, out var petJsonUri))
        {
            return null;
        }

        return new PetdexCatalogEntry(
            slug,
            GetManifestField(content, "displayName") ?? slug,
            GetManifestField(content, "description") ?? string.Empty,
            GetManifestField(content, "kind"),
            GetManifestField(content, "submittedBy"),
            new Uri($"https://petdex.dev/pets/{Uri.EscapeDataString(slug)}"),
            petJsonUri,
            spriteSheetUri,
            Array.Empty<string>());
    }

    private static IReadOnlyList<PetdexCatalogEntry> Sort(IEnumerable<PetdexCatalogEntry> entries) => entries
        .GroupBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static string? GetManifestField(string content, string name)
    {
        var match = Regex.Match(content, $"\\\"{Regex.Escape(name)}\\\"\\s*:\\s*\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static (string DisplayName, string Description) ParsePackageMetadata(string packageJson, PetdexCatalogEntry entry)
    {
        using var document = JsonDocument.Parse(packageJson);
        var root = document.RootElement;
        return (
            NullIfEmpty(GetString(root, "displayName")) ?? entry.DisplayName,
            NullIfEmpty(GetString(root, "description")) ?? entry.Description);
    }

    private static IReadOnlyList<string> GetActions(JsonElement pet)
    {
        foreach (var propertyName in new[] { "animationStates", "states", "animations" })
        {
            if (!TryGetProperty(pet, propertyName, out var actions))
            {
                continue;
            }

            if (actions.ValueKind == JsonValueKind.Array)
            {
                return actions.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()!)
                    .ToArray();
            }

            if (actions.ValueKind == JsonValueKind.Object)
            {
                return actions.EnumerateObject().Select(property => property.Name).ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static string GetString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement result)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                result = property.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void EnsureHttps(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Petdex downloads must use HTTPS.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is >= 300 and < 400;
}
