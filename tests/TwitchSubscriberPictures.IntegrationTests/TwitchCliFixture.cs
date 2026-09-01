using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchSubscriberPictures.IntegrationTests;

[CollectionDefinition("Twitch CLI")]
public sealed class TwitchCliCollection : ICollectionFixture<TwitchCliFixture>
{
}

public sealed class TwitchCliFixture : IAsyncLifetime
{
    private Process? _mockApiProcess;
    private Process? _webSocketProcess;
    private readonly HttpClient _http = new();

    public string TwitchCliPath { get; private set; } = string.Empty;
    public int MockApiPort { get; private set; }
    public int WebSocketPort { get; private set; }
    public string MockApiBaseUrl => $"http://127.0.0.1:{MockApiPort}";
    public string WebSocketBaseUrl => $"ws://127.0.0.1:{WebSocketPort}";

    public async Task InitializeAsync()
    {
        TwitchCliPath = FindTwitchCli();
        MockApiPort = GetFreeTcpPort();
        WebSocketPort = GetFreeTcpPort();

        RunTwitchCli($"mock-api generate -c 10");

        _mockApiProcess = StartTwitchCli($"mock-api start -p {MockApiPort}");
        _webSocketProcess = StartTwitchCli($"event websocket start-server -p {WebSocketPort}");

        await WaitForMockApiAsync();
        await WaitForWebSocketServerAsync();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();

        if (_mockApiProcess is not null && !_mockApiProcess.HasExited)
        {
            _mockApiProcess.Kill(entireProcessTree: true);
            await _mockApiProcess.WaitForExitAsync();
        }

        if (_webSocketProcess is not null && !_webSocketProcess.HasExited)
        {
            _webSocketProcess.Kill(entireProcessTree: true);
            await _webSocketProcess.WaitForExitAsync();
        }
    }

    public async Task<MockClient> GetFirstClientAsync()
    {
        using var document = await GetJsonAsync("/units/clients");
        var data = GetDataArray(document);
        var item = data.EnumerateArray().First();

        return new MockClient(
            item.GetProperty("ID").GetString()!,
            item.GetProperty("Secret").GetString()!);
    }

    public async Task<MockSubscription> GetFirstSubscriptionAsync()
    {
        using var document = await GetJsonAsync("/units/subscriptions");
        var data = GetDataArray(document);
        var item = data.EnumerateArray().First();

        return new MockSubscription(
            item.GetProperty("broadcaster_id").GetString()!,
            item.GetProperty("user_id").GetString()!,
            item.GetProperty("user_login").GetString()!);
    }

    public async Task TriggerWebSocketEventAsync(string eventName, string sessionId)
    {
        var process = StartTwitchCli(
            $"event trigger {eventName} --transport=websocket --session={sessionId}");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Twitch CLI event trigger failed with exit code {process.ExitCode}.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        var response = await _http.GetAsync(MockApiBaseUrl + path);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static JsonElement GetDataArray(JsonDocument document)
    {
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Array
            ? root
            : root.GetProperty("data");
    }

    private void RunTwitchCli(string arguments)
    {
        var process = StartTwitchCli(arguments);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Twitch CLI command failed with exit code {process.ExitCode}: {arguments}");
        }
    }

    private Process StartTwitchCli(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TwitchCliPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(TwitchCliPath)!
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Twitch CLI.");
    }

    private async Task WaitForMockApiAsync()
    {
        for (var i = 0; i < 80; i++)
        {
            try
            {
                var response = await _http.GetAsync($"{MockApiBaseUrl}/units/clients");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Twitch CLI mock API did not start in time.");
    }

    private async Task WaitForWebSocketServerAsync()
    {
        using var socket = new ClientWebSocket();

        for (var i = 0; i < 80; i++)
        {
            try
            {
                await socket.ConnectAsync(new Uri($"{WebSocketBaseUrl}/ws"), CancellationToken.None);
                return;
            }
            catch (WebSocketException)
            {
                await Task.Delay(250);
            }
        }

        throw new TimeoutException("Twitch CLI WebSocket server did not start in time.");
    }

    private static string FindTwitchCli()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var match = directory
                .GetDirectories("twitch-cli_*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (match is not null)
            {
                var executable = Path.Combine(match.FullName, "twitch.exe");
                if (File.Exists(executable))
                {
                    return executable;
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the twitch-cli_* directory from the test output directory.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed record MockClient(string Id, string Secret);

public sealed record MockSubscription(string BroadcasterId, string UserId, string UserLogin);
