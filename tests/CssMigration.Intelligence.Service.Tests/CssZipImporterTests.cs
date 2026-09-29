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
    public void Import_CompilesLessPerTenantBeforeAnalysis()
    {
        var compiler = new RecordingLessCompiler(".module-home__hero { color: #ffffff; } .module-home__hero:hover { color: #ffffff; } .module-home__hero:focus { color: #ffffff; }");
        using var stream = Zip(
            ("north/variables.less", "@brand: #fff;"),
            ("north/home.less", "@import \"variables.less\"; .module-home { &__hero { color: @brand; } }"));

        var result = new CssZipImporter(compiler).Import(stream);

        Assert.Contains("Internal import variables.less", compiler.ReceivedSource);
        Assert.Equal("zip-less-compiled", Assert.Single(result.Portfolio.Sources).Version);
        Assert.Equal("LESS compiled to CSS", Assert.Single(result.Mappings).InputFormat);
        Assert.Contains(result.Analysis.TokenPlan.Candidates, item => item.Name.Contains("module-home", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_RejectsExternalLessImports()
    {
        using var stream = Zip(("north/home.less", "@import \"https://example.test/theme.less\"; .card { color: red; }"));
        var error = Assert.Throws<PortfolioValidationException>(() => new CssZipImporter(new RecordingLessCompiler("")).Import(stream));
        Assert.Contains(error.Errors, item => item.Code == "less.import.external");
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

    [Fact]
    public void Import_AcceptsStylesheetsAboveTheFormerDemoLimit()
    {
        var css = $"/* {new string('x', 300_000)} */ .module-home__hero {{ color: #123456; }}";
        using var stream = Zip(("north/home.css", css));

        var result = new CssZipImporter().Import(stream);

        Assert.Equal(1, result.Analysis.TenantCount);
        Assert.True(Assert.Single(result.Mappings).CssCharacters > 250_000);
    }

    [Fact]
    public void LessCompilerError_PrefersFatalDiagnosticAfterDeprecationWarnings()
    {
        const string stderr = "DEPRECATED WARNING: legacy media syntax\n109 @media @mobile {\nSyntaxError: expected ')' in - on line 212, column 4";

        var result = NodeLessCompiler.RelevantCompilerError(stderr);

        Assert.StartsWith("SyntaxError:", result);
        Assert.DoesNotContain("DEPRECATED WARNING", result);
    }

    [Fact]
    public void Import_RepairsLegacyDropShadowFunctionSpacing()
    {
        var compiler = new RecordingLessCompiler(".tile { filter: drop-shadow(0 2px 4px #0003); }");
        using var stream = Zip(("convini/theme.less", ".tile { filter: drop-shadow (0 2px 4px rgba(0,0,0,.2)); }"));

        var result = new CssZipImporter(compiler).Import(stream);

        Assert.Contains("drop-shadow(0 2px", compiler.ReceivedSource);
        Assert.DoesNotContain("drop-shadow (", compiler.ReceivedSource);
        Assert.Single(Assert.Single(result.Mappings).CompatibilityRepairs);
    }

    [Fact]
    public void Import_ReturnsValidTenantsAndReportsTenantCompilationFailures()
    {
        using var stream = Zip(
            ("valid/theme.less", ".card { color: red; }"),
            ("broken/theme.less", ".card { color: @missing; }"));
        var compiler = new SelectiveLessCompiler();

        var result = new CssZipImporter(compiler).Import(stream);

        Assert.Equal("valid", Assert.Single(result.Portfolio.Sources).TenantKey);
        Assert.Equal("broken", Assert.Single(result.Failures).TenantKey);
        Assert.Contains(Assert.Single(result.Failures).Errors, error => error.Code == "less.compile.failed");
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

    private sealed class RecordingLessCompiler(string output) : ILessCompiler
    {
        public string ReceivedSource { get; private set; } = string.Empty;

        public string Compile(string less, string sourceLabel)
        {
            ReceivedSource = less;
            return output;
        }
    }

    private sealed class SelectiveLessCompiler : ILessCompiler
    {
        public string Compile(string less, string sourceLabel)
        {
            if (sourceLabel == "broken")
                throw new PortfolioValidationException([new("less.compile.failed", "Synthetic compilation failure.")]);
            return ".card { color: red; }";
        }
    }
}
