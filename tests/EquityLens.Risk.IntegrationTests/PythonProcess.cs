using System.Diagnostics;

namespace EquityLens.Risk.IntegrationTests;

internal sealed class PythonProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    public static string Root { get; } = FindRoot();

    public PythonProcess(IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("RISK_TEST_PYTHON") ?? "python3")
        {
            WorkingDirectory = Root, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["PYTHONPATH"] = Path.Combine(Root, "src/EquityLens.Mathematics/src");
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["OPENBLAS_NUM_THREADS"] = "1";
        start.Environment["OMP_NUM_THREADS"] = "1";
        if (environment is not null)
            foreach (var (key, value) in environment) start.Environment[key] = value;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Python");
        _stdout = _process.StandardOutput.ReadToEndAsync();
        _stderr = _process.StandardError.ReadToEndAsync();
    }

    public bool HasExited => _process.HasExited;
    public async Task<(int ExitCode, string Output, string Error)> RunAsync(string input)
    {
        await _process.StandardInput.WriteAsync(input);
        _process.StandardInput.Close();
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        return (_process.ExitCode, await _stdout, await _stderr);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var directory = Environment.GetEnvironmentVariable("RISK_TEST_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"python-{_process.Id}.log"), await _stdout + "\n" + await _stderr);
        }
        _process.Dispose();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
