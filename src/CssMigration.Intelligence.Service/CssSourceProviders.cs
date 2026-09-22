// File purpose: Defines the CSS input abstraction and in-memory, inline-JSON, and file-manifest provider implementations.
using System.Text.Json;

namespace CssMigration.Intelligence.Service;

public interface ICssSourceProvider
{
    Task<CssPortfolioInput> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryCssSourceProvider(CssPortfolioInput input) : ICssSourceProvider
{
    public Task<CssPortfolioInput> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(input);
}

public sealed class JsonCssSourceProvider(string jsonPath) : ICssSourceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CssPortfolioInput> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(jsonPath);
        var input = await JsonSerializer.DeserializeAsync<CssPortfolioInput>(stream, JsonOptions, cancellationToken);
        return input ?? throw new PortfolioValidationException([
            new PortfolioValidationError("portfolio.empty", "The JSON document did not contain a portfolio.")
        ]);
    }
}

public sealed record CssPortfolioManifest(
    string SchemaVersion,
    DateTimeOffset ExportedAt,
    IReadOnlyList<CssPortfolioManifestSource> Sources);

public sealed record CssPortfolioManifestSource(
    string TenantKey,
    string DisplayName,
    string SourceId,
    string Version,
    DateTimeOffset UpdatedAt,
    string CssPath);

public sealed class FileManifestCssSourceProvider(string manifestPath) : ICssSourceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CssPortfolioInput> LoadAsync(CancellationToken cancellationToken = default)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new IOException("The portfolio manifest directory could not be resolved.");
        await using var stream = File.OpenRead(fullManifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CssPortfolioManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new PortfolioValidationException([
                new PortfolioValidationError("portfolio.empty", "The manifest did not contain a portfolio.")
            ]);

        var sources = new List<CssPortfolioSource>(manifest.Sources.Count);
        foreach (var source in manifest.Sources)
        {
            var cssPath = Path.GetFullPath(Path.Combine(manifestDirectory, source.CssPath));
            var allowedPrefix = manifestDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? manifestDirectory
                : manifestDirectory + Path.DirectorySeparatorChar;
            if (!cssPath.StartsWith(allowedPrefix, StringComparison.Ordinal))
                throw new PortfolioValidationException([
                    new PortfolioValidationError("portfolio.css_path.invalid", $"CSS path '{source.CssPath}' escapes the manifest directory.")
                ]);

            var css = await File.ReadAllTextAsync(cssPath, cancellationToken);
            sources.Add(new CssPortfolioSource(
                source.TenantKey,
                source.DisplayName,
                source.SourceId,
                source.Version,
                source.UpdatedAt,
                css));
        }

        return new CssPortfolioInput(manifest.SchemaVersion, manifest.ExportedAt, sources);
    }
}
