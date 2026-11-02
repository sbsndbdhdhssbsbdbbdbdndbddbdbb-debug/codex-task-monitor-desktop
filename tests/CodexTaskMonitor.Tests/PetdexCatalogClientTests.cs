using System.Net;
using System.Text;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class PetdexCatalogClientTests
{
    [Fact]
    public void UsesTheCurrentOfficialCatalogEndpoint() =>
        Assert.Equal("https://petdex.dev/api/manifest", PetdexCatalogClient.ManifestUri.AbsoluteUri);

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task RetriesATemporaryCatalogFailureOnce(HttpStatusCode status)
    {
        var handler = new CatalogHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        var client = new PetdexCatalogClient(new HttpClient(handler));

        var catalog = await client.GetCatalogAsync();

        Assert.Equal("goku", Assert.Single(catalog).Slug);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task RetriesATemporaryConnectionFailure()
    {
        var handler = new CatalogHandler((_, _) => throw new HttpRequestException("Connection interrupted."));
        var client = new PetdexCatalogClient(new HttpClient(handler));

        Assert.Single(await client.GetCatalogAsync());
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task RetriesATimeoutWithoutConfusingItWithUserCancellation()
    {
        var handler = new CatalogHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new PetdexCatalogClient(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) });

        Assert.Single(await client.GetCatalogAsync());
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DoesNotRetryPermanentCatalogFailures(HttpStatusCode status)
    {
        var handler = new CatalogHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        var client = new PetdexCatalogClient(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCatalogAsync());

        Assert.Equal(status, error.StatusCode);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task CancellingTheCatalogDoesNotStartARetry()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new CatalogHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var client = new PetdexCatalogClient(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCatalogAsync(cancellation.Token));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ReadsTheOfficialManifestAndDownloadsOnlyItsPackageFiles()
    {
        var responses = new Dictionary<string, byte[]>
        {
            [PetdexCatalogClient.ManifestUri.AbsoluteUri] = Encoding.UTF8.GetBytes("""
                { "pets": [{
                    "slug": "goku",
                    "displayName": "Goku",
                    "description": "A focused companion.",
                    "kind": "anime",
                    "submittedBy": "Petdex",
                    "spritesheetUrl": "https://cdn.example.test/goku/spritesheet.png",
                    "petJsonUrl": "https://cdn.example.test/goku/pet.json",
                    "animationStates": ["idle", "run", "wave"]
                }] }
                """),
            ["https://cdn.example.test/goku/pet.json"] = Encoding.UTF8.GetBytes("""
                { "id": "goku", "displayName": "Goku", "description": "A focused companion.", "spritesheetPath": "spritesheet.png" }
                """),
            ["https://cdn.example.test/goku/spritesheet.png"] = CreatePng(),
        };
        var client = new PetdexCatalogClient(new HttpClient(new DictionaryHandler(responses)));

        var catalog = await client.GetCatalogAsync();
        var download = await client.DownloadAsync(Assert.Single(catalog));

        Assert.Equal("goku", download.Package.Id);
        Assert.Equal("Goku", download.Package.Name);
        Assert.Contains("run", download.Package.Actions);
        Assert.Equal("https://petdex.dev/pets/goku", download.Package.SourcePageUrl);
        Assert.Equal(CreatePng(), download.SpriteSheetBytes);
    }

    [Fact]
    public async Task KeepsReadingEntriesWhenOneManifestLabelIsMalformed()
    {
        var responses = new Dictionary<string, byte[]>
        {
            [PetdexCatalogClient.ManifestUri.AbsoluteUri] = Encoding.UTF8.GetBytes("""
                { "pets": [{
                    "slug": "goku", "displayName": "Goku, "kind": "character",
                    "spritesheetUrl": "https://cdn.example.test/goku/spritesheet.png",
                    "petJsonUrl": "https://cdn.example.test/goku/pet.json"
                }] }
                """),
        };
        var client = new PetdexCatalogClient(new HttpClient(new DictionaryHandler(responses)));

        var catalog = await client.GetCatalogAsync();

        Assert.Equal("goku", Assert.Single(catalog).Slug);
    }

    [Fact]
    public async Task FollowsAnHttpsCatalogRedirect()
    {
        var redirectUri = new Uri("https://assets.petdex.dev/manifests/petdex-v1.json");
        var handler = new RedirectingHandler(redirectUri, Encoding.UTF8.GetBytes("""
            { "pets": [{ "slug": "goku", "displayName": "Goku", "spritesheetUrl": "https://cdn.example.test/goku/s.png", "petJsonUrl": "https://cdn.example.test/goku/p.json" }] }
            """));
        var client = new PetdexCatalogClient(new HttpClient(handler));

        var catalog = await client.GetCatalogAsync();

        Assert.Equal("goku", Assert.Single(catalog).Slug);
    }

    [Theory]
    [InlineData("goku", "goku")]
    [InlineData("https://petdex.dev/pets/goku", "goku")]
    [InlineData("npx petdex@latest install goku", "goku")]
    public void AcceptsPetdexSlugOrPageUrl(string value, string expected) =>
        Assert.Equal(expected, PetdexCatalogClient.NormalizeSlugOrUrl(value));

    private static byte[] CreatePng() =>
    [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 80, 0, 0, 0, 90];

    private sealed class CatalogHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> firstResponse) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Requests == 1 ? firstResponse(request, cancellationToken) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    { "pets": [{ "slug": "goku", "displayName": "Goku", "spritesheetUrl": "https://cdn.example.test/goku/s.png", "petJsonUrl": "https://cdn.example.test/goku/p.json" }] }
                    """),
            });
        }
    }

    private sealed class DictionaryHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            });
        }
    }

    private sealed class RedirectingHandler(Uri redirectUri, byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri == PetdexCatalogClient.ManifestUri)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PermanentRedirect)
                {
                    Headers = { Location = redirectUri },
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }
}
