using System.IO.Compression;
using CssMigration.Intelligence.Service;

namespace CssMigration.Intelligence.Service.Tests;

public sealed class CssZipImporterTests
{
    [Fact]
    public void Import_GroupsMultipleFilesPerTenantAndReportsModules()
    {
        using var stream = Zip(
            ("north/home.css", ".module-home__hero { color: #fff; background: #125599; }"),
            ("north/learning.css", ".module-learning__card { color: #111; display: grid; }"),
            ("south/home.css", ".module-home__hero { color: #fff; background: #882244; }"));

        var result = new CssZipImporter().Import(stream);

        Assert.Equal(2, result.Analysis.TenantCount);
        Assert.Equal(2, result.Mappings.Single(item => item.TenantKey == "north").Files.Count);
        Assert.Contains(result.Analysis.Modules, item => item.Module == CssModuleClassifier.Home && item.TotalDeclarationCount == 4);
        Assert.Contains(result.Analysis.Modules, item => item.Module == CssModuleClassifier.Learning && item.TotalDeclarationCount == 2);
        Assert.Contains(result.Analysis.TokenPlan.Candidates, item => item.Name.Contains("module-home", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_RejectsArchiveTraversal()
    {
        using var stream = Zip(("../outside.css", ".card { color: red; }"));
        var error = Assert.Throws<PortfolioValidationException>(() => new CssZipImporter().Import(stream));
        Assert.Contains(error.Errors, item => item.Code == "zip.path.invalid");
    }

    [Fact]
    public void Import_RejectsNonCssFiles()
    {
        using var stream = Zip(("north/secret.txt", "not CSS"));
        var error = Assert.Throws<PortfolioValidationException>(() => new CssZipImporter().Import(stream));
        Assert.Contains(error.Errors, item => item.Code == "zip.file.unsupported");
    }

    [Fact]
    public void Import_UsesSuppliedClientNameWithoutChangingTechnicalTenantKey()
    {
        using var stream = Zip(("synthetic-north/home.css", ".module-home { color: #123456; }"));
        var result = new CssZipImporter().Import(stream,
            new Dictionary<string, string> { ["synthetic-north"] = "Northstar Outfitters (demo)" });

        Assert.Equal("synthetic-north", Assert.Single(result.Portfolio.Sources).TenantKey);
        Assert.Equal("Northstar Outfitters (demo)", Assert.Single(result.Mappings).DisplayName);
        Assert.Equal("Northstar Outfitters (demo)", Assert.Single(result.Analysis.Tenants).DisplayName);
    }

    private static MemoryStream Zip(params (string Path, string Css)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, css) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(css);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
