using System.Diagnostics;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Owns an official Everything SSE server installed from the repository npm lockfile.
/// </summary>
public sealed class EverythingSseServerFixture(int port) : IAsyncDisposable
{
    private Process? _process;
    private Task<string>? _output;
    private Task<string>? _error;

    /// <summary>
    /// Starts the server, drains diagnostic pipes, and fails explicitly if it cannot become ready.
    /// </summary>
    public async Task StartAsync()
    {
        _process = Process.Start(NodeHelpers.EverythingServerStartInfo(port))
            ?? throw new InvalidOperationException("Could not start the pinned Everything server.");
        _output = _process.StandardOutput.ReadToEndAsync();
        _error = _process.StandardError.ReadToEndAsync();
        using var client = new HttpClient { Timeout = TestConstants.HttpClientPollingTimeout };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Everything server exited with {_process.ExitCode}: {await _error}");
            try
            {
                using var response = await client.GetAsync($"http://localhost:{port}/sse", HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                // Startup has not opened the listener yet; a process exit is checked on every retry.
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Pinned Everything server did not become ready on port {port}.");
    }

    /// <summary>
    /// Stops the owned Node process and observes diagnostic readers even after a failed test.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill();
            await _process.WaitForExitAsync(TestConstants.DefaultTimeout);
            if (_output is not null) await _output;
            if (_error is not null) await _error;
        }
        finally { _process.Dispose(); }
    }
}
