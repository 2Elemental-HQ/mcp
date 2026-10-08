using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests;

public class EverythingSseServerTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    /// <summary>Port number to be grabbed by the next test.</summary>
    private static int s_nextPort = 3000;

    // If the tests run concurrently against different versions of the runtime, tests can conflict with
    // each other in the ports set up for interacting with child servers. Ensure that such suites running
    // against different TFMs use different port numbers.
    private static readonly int s_portOffset = 1000 * (Environment.Version.Major switch
    {
        int v when v >= 8 => Environment.Version.Major - 7,
        _ => 0,
    });

    private static int CreatePortNumber() => Interlocked.Increment(ref s_nextPort) + s_portOffset;

    [Fact]
    public async Task ConnectAndReceiveMessage_EverythingServerWithSse()
    {
        int port = CreatePortNumber();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestConstants.DefaultTimeout);
        using var http = new HttpClient();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "IntegrationTestClient", Version = "1.0.0" }
        };

        var defaultConfig = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/sse"),
            Name = "Everything",
        };

        // Create client and run tests
        TestOutputHelper.WriteLine("SSE fixture ready; creating SDK client.");
        var client = await McpClient.CreateAsync(
            new HttpClientTransport(defaultConfig, http),
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: timeout.Token).WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        try
        {
            TestOutputHelper.WriteLine("Client initialized; requesting tools.");
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.NotEmpty(tools);
            TestOutputHelper.WriteLine("Complete tools response received.");
        }
        finally { await DisposeClientAsync(client, http); }

    }

    [Fact]
    public async Task Sampling_Sse_EverythingServer()
    {
        int port = CreatePortNumber();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestConstants.DefaultTimeout);
        using var http = new HttpClient();

        await using var fixture = new EverythingSseServerFixture(port);
        await fixture.StartAsync();

        var defaultConfig = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/sse"),
            Name = "Everything",
        };

        int samplingHandlerCalls = 0;
        var defaultOptions = new McpClientOptions()
        {
            Handlers = new()
            {
                SamplingHandler = async (_, _, _) =>
                {
                    samplingHandlerCalls++;
                    return new CreateMessageResult
                    {
                        Model = "test-model",
                        Role = Role.Assistant,
                        Content = [new TextContentBlock { Text = "Test response" }],
                    };
                }
            }
        };

        TestOutputHelper.WriteLine("SSE fixture ready; creating SDK client.");
        var client = await McpClient.CreateAsync(
            new HttpClientTransport(defaultConfig, http),
            defaultOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: timeout.Token).WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        try
        {
            TestOutputHelper.WriteLine("Client initialized; requesting sampling.");
            // Call the server's trigger-sampling-request tool which should trigger our sampling handler
            var result = await client.CallToolAsync("trigger-sampling-request", new Dictionary<string, object?>
                {
                    ["prompt"] = "Test prompt",
                    ["maxTokens"] = 100
                }, cancellationToken: timeout.Token);

            // assert
            Assert.NotNull(result);
            Assert.Equal(1, samplingHandlerCalls);
            var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
            Assert.Equal("text", textContent.Type);
            Assert.False(string.IsNullOrEmpty(textContent.Text));
            TestOutputHelper.WriteLine("Complete sampling response received.");
        }
        finally { await DisposeClientAsync(client, http); }

    }

    /// <summary>
    /// Reports a stuck SDK shutdown as a failure, then releases the owned HTTP connection for cleanup.
    /// </summary>
    private async Task DisposeClientAsync(McpClient client, HttpClient http)
    {
        TestOutputHelper.WriteLine("Disposing SDK client.");
        var disposal = client.DisposeAsync().AsTask();
        try
        {
            await disposal.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            TestOutputHelper.WriteLine("SDK client disposed.");
        }
        finally
        {
            // Cleanup must not hide a timeout: WaitAsync's failure still leaves this method.
            http.Dispose();
        }
    }
}
