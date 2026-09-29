// File purpose: Compiles untrusted LESS without JavaScript before deterministic CSS analysis.
using System.Diagnostics;

namespace CssMigration.Intelligence.Service;

public interface ILessCompiler
{
    string Compile(string less, string sourceLabel);
}

public sealed class NodeLessCompiler : ILessCompiler
{
    private const int TimeoutMilliseconds = 20_000;
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
            Arguments = "--no-js --no-color --math=parens-division -",
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
            var detail = standardError.Result.Trim();
            if (detail.Length > 700) detail = detail[..700] + "…";
            throw Error("less.compile.failed", $"LESS compilation failed for {sourceLabel}: {detail}");
        }

        return standardOutput.Result;
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
