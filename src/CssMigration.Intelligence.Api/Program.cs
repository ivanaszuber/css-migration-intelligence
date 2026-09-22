using System.Text.Json;
using System.ClientModel;
using System.IO.Compression;
// File purpose: Composes the ASP.NET Core host, dependencies, static Angular files, health check, and all HTTP endpoints.
using CssMigration.Intelligence.Api;
using CssMigration.Intelligence.Service;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 13_000_000);
builder.Services.AddSingleton<CssPortfolioAnalyzer>();
builder.Services.AddSingleton<CssMigrationCompiler>();
builder.Services.AddSingleton<TenantThemeCompiler>();
builder.Services.AddSingleton<CssZipImporter>();
var azureOpenAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var azureOpenAiDeployment = builder.Configuration["AzureOpenAI:Deployment"];
if (Uri.TryCreate(azureOpenAiEndpoint, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(azureOpenAiDeployment))
    builder.Services.AddSingleton<IAiRecommendationProvider>(new AzureOpenAiRecommendationProvider(azureOpenAiEndpoint!, azureOpenAiDeployment));
else
    builder.Services.AddSingleton<IAiRecommendationProvider, DisabledAiRecommendationProvider>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:4500")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var configuredPortfolioPath = builder.Configuration["Portfolio:JsonPath"];
var portfolioPath = string.IsNullOrWhiteSpace(configuredPortfolioPath)
    ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "samples", "portfolio.manifest.json"))
    : configuredPortfolioPath;
builder.Services.AddSingleton<ICssSourceProvider>(_ => new FileManifestCssSourceProvider(portfolioPath));

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" || context.Request.Path == "/index.html")
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.CacheControl = "no-store";
    }
});

app.MapGet("/health", (IAiRecommendationProvider aiProvider) => Results.Ok(new
{
    status = "healthy",
    service = "css-migration-intelligence",
    persistence = "none",
    ai = aiProvider.IsConfigured ? "configured" : "disabled",
    aiProvider = aiProvider.ProviderName
}));

app.MapPost("/api/analyze", (CssAnalysisRequest request, CssPortfolioAnalyzer analyzer) =>
{
    try { return Results.Ok(analyzer.AnalyzeSingle(request.SourceId, request.Css)); }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
});

app.MapPost("/api/portfolio/ai-recommendations", async (
    CssPortfolioInput input,
    CssPortfolioAnalyzer analyzer,
    IAiRecommendationProvider aiProvider,
    CancellationToken cancellationToken) =>
{
    try
    {
        var analysis = analyzer.AnalyzePortfolio(input);
        return Results.Ok(await aiProvider.RecommendAsync(analysis.TokenPlan, cancellationToken));
    }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
    catch (JsonException exception) { return Results.Problem(title: "Azure AI returned an invalid recommendation response.", detail: exception.Message, statusCode: 502); }
    catch (ClientResultException exception) when (exception.Status == 429) { return Results.Problem(title: "Azure AI rate limit reached.", detail: "The sandbox model quota is temporarily exhausted. Retry after the Azure rate-limit window resets.", statusCode: 429); }
    catch (ClientResultException exception) { return Results.Problem(title: "Azure AI request failed.", detail: $"Azure returned HTTP {exception.Status}.", statusCode: exception.Status > 0 ? exception.Status : 502); }
});

app.MapGet("/api/portfolio/ai-recommendations", async (
    ICssSourceProvider provider,
    CssPortfolioAnalyzer analyzer,
    IAiRecommendationProvider aiProvider,
    CancellationToken cancellationToken) =>
{
    try
    {
        var input = await provider.LoadAsync(cancellationToken);
        var analysis = analyzer.AnalyzePortfolio(input);
        return Results.Ok(await aiProvider.RecommendAsync(analysis.TokenPlan, cancellationToken));
    }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
    catch (JsonException exception) { return Results.Problem(title: "Azure AI returned an invalid recommendation response.", detail: exception.Message, statusCode: 502); }
    catch (ClientResultException exception) when (exception.Status == 429) { return Results.Problem(title: "Azure AI rate limit reached.", detail: "The sandbox model quota is temporarily exhausted. Retry after the Azure rate-limit window resets.", statusCode: 429); }
    catch (ClientResultException exception) { return Results.Problem(title: "Azure AI request failed.", detail: $"Azure returned HTTP {exception.Status}.", statusCode: exception.Status > 0 ? exception.Status : 502); }
});

app.MapPost("/api/portfolio/analyze", (CssPortfolioInput input, CssPortfolioAnalyzer analyzer) =>
{
    try { return Results.Ok(analyzer.AnalyzePortfolio(input)); }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
});

app.MapPost("/api/portfolio/import-zip", (IFormFile file, CssZipImporter importer) =>
{
    if (file.Length == 0 || file.Length > CssZipImporter.MaximumArchiveBytes)
        return Results.BadRequest(new { errors = new[] { new PortfolioValidationError("zip.size", "Choose a ZIP archive under 12 MB.") } });
    try
    {
        using var stream = file.OpenReadStream();
        return Results.Ok(importer.Import(stream));
    }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
}).DisableAntiforgery();

app.MapGet("/api/portfolio/demo-import", async (
    ICssSourceProvider provider, CssZipImporter importer, CancellationToken cancellationToken) =>
{
    var portfolio = await provider.LoadAsync(cancellationToken);
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var source in portfolio.Sources)
        {
            var entry = archive.CreateEntry($"{source.TenantKey}/{source.SourceId}");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync(source.Css);
        }
    }
    stream.Position = 0;
    return Results.Ok(importer.Import(stream, portfolio.Sources.ToDictionary(source => source.TenantKey,
        source => source.DisplayName, StringComparer.OrdinalIgnoreCase)));
});

app.MapPost("/api/portfolio/migration-bundle", async (
    CssMigrationBundleRequest request,
    ICssSourceProvider provider,
    CssMigrationCompiler compiler,
    CancellationToken cancellationToken) =>
{
    try
    {
        var input = request.Portfolio ?? await provider.LoadAsync(cancellationToken);
        return Results.Ok(compiler.Compile(input, request));
    }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
    catch (JsonException exception) { return Results.BadRequest(new { errors = new[] { new { code = "portfolio.json.invalid", message = exception.Message } } }); }
    catch (IOException exception) { return Results.Problem(title: "Portfolio input could not be read.", detail: exception.Message, statusCode: 503); }
});

app.MapGet("/api/theme/presets", (TenantThemeCompiler compiler) => Results.Ok(compiler.Presets()));

app.MapPost("/api/theme/compile", (TenantThemeConfiguration configuration, TenantThemeCompiler compiler) =>
    Results.Ok(compiler.Compile(configuration)));

app.MapPost("/api/theme/project-from-migration", (MigratedThemeProjectionRequest request, TenantThemeCompiler compiler) =>
    Results.Ok(compiler.Project(request)));

app.MapGet("/api/portfolio", async (ICssSourceProvider provider, CssPortfolioAnalyzer analyzer, CancellationToken cancellationToken) =>
{
    try
    {
        var input = await provider.LoadAsync(cancellationToken);
        return Results.Ok(analyzer.AnalyzePortfolio(input));
    }
    catch (PortfolioValidationException exception) { return Results.BadRequest(new { errors = exception.Errors }); }
    catch (JsonException exception) { return Results.BadRequest(new { errors = new[] { new { code = "portfolio.json.invalid", message = exception.Message } } }); }
    catch (IOException exception) { return Results.Problem(title: "Portfolio input could not be read.", detail: exception.Message, statusCode: 503); }
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
