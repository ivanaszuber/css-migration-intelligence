// File purpose: Compiles untrusted LESS without JavaScript before deterministic CSS analysis.
using System.Diagnostics;

namespace CssMigration.Intelligence.Service;

public interface ILessCompiler
{
    string Compile(string less, string sourceLabel);
}

public sealed class NodeLessCompiler : ILessCompiler
{
    private const int TimeoutMilliseconds = 90_000;
    private readonly string _compilerPath;

    public NodeLessCompiler(string? compilerPath = null)
    {
        _compilerPath = compilerPath ?? ResolveCompilerPath();
    }

    public string Compile(string less, string sourceLabel)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _compilerPath,
            // Inline JavaScript is disabled by default in modern lessc. Passing
            // --no-js now emits a deprecation warning for every compilation.
            Arguments = "--no-color --math=parens-division -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw Error("less.compiler.unavailable", "LESS compilation is unavailable on this host. Configure LESSC_PATH or install the pinned less compiler.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(less);
        process.StandardInput.Close();

        if (!process.WaitForExit(TimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw Error("less.compiler.timeout", $"LESS compilation timed out for {sourceLabel}.");
        }

        Task.WaitAll(standardOutput, standardError);
        if (process.ExitCode != 0)
        {
            var detail = RelevantCompilerError(standardError.Result);
            throw Error("less.compile.failed", $"LESS compilation failed for {sourceLabel}: {detail}");
        }

        return standardOutput.Result;
    }

    internal static string RelevantCompilerError(string standardError)
    {
        var detail = standardError.Trim();
        if (detail.Length == 0) return "The compiler exited without an error message.";

        // lessc writes deprecation warnings before the fatal diagnostic. Prefer
        // the final named error so callers see the actionable line and column.
        var namedErrors = new[]
        {
            "SyntaxError:", "ParseError:", "NameError:", "FileError:",
            "RuntimeError:", "OperationError:", "ArgumentError:", "Error:"
        };
        var errorStart = namedErrors
            .Select(marker => detail.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
                ? 0
                : detail.LastIndexOf($"\n{marker}", StringComparison.OrdinalIgnoreCase) is var index && index >= 0
                    ? index + 1
                    : -1)
            .Max();
        if (errorStart >= 0) detail = detail[errorStart..];

        const int maximumDetailLength = 1_500;
        if (detail.Length > maximumDetailLength)
            detail = "…" + detail[^maximumDetailLength..];
        return detail;
    }

    private static string ResolveCompilerPath()
    {
        var configured = Environment.GetEnvironmentVariable("LESSC_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var local = Path.Combine(current.FullName, "web", "node_modules", ".bin", "lessc");
            if (File.Exists(local)) return local;
            current = current.Parent;
        }

        return "lessc";
    }

    private static PortfolioValidationException Error(string code, string message)
        => new([new PortfolioValidationError(code, message)]);
}
