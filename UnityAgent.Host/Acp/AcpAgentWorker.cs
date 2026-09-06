using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Host.Acp;

internal sealed class AcpAgentWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IAgentRuntime _agent;
    private readonly AgentSettings _settings;

	private readonly ConcurrentDictionary<string, ProjectWorkspace> _sessions = new();    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AcpAgentWorker(IAgentRuntime agent, IOptions<AgentSettings> settings)
    {
        _agent = agent;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(stoppingToken);

            if (line is null)
                break;

            await HandleMessageAsync(line, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(string line, CancellationToken stoppingToken)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("method", out var methodElement))
                return;

            var method = methodElement.GetString();
            var hasId = root.TryGetProperty("id", out var idElement);
            var id = hasId ? idElement.Clone() : default;

            root.TryGetProperty("params", out var parameters);

            switch (method)
            {
                case "initialize":
                    await HandleInitializeAsync(id, stoppingToken);
                    break;

                case "session/new":
                    await HandleNewSessionAsync(id, parameters, stoppingToken);
                    break;

                case "session/prompt":
                    await HandlePromptAsync(id, parameters, stoppingToken);
                    break;

                case "session/cancel":
                    HandleCancel(parameters);
                    break;

                default:
                    if (hasId)
                        await SendErrorAsync(id, -32601, $"Unsupported ACP method: {method}", stoppingToken);
                    break;
            }
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"Invalid ACP JSON: {exception.Message}");
        }
    }

    private Task HandleInitializeAsync(JsonElement id, CancellationToken cancellationToken)
    {
        var result = new
        {
            protocolVersion = 1,
            agentCapabilities = new
            {
                loadSession = false
            },
            agentInfo = new
            {
                name = "unity-agent",
                title = "UnityAgent",
                version = "0.1.0"
            },
            authMethods = Array.Empty<object>()
        };

        return SendResultAsync(id, result, cancellationToken);
    }

    private Task HandleNewSessionAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
		var cwd = parameters.GetProperty("cwd").GetString();

		if (string.IsNullOrWhiteSpace(cwd))
			return SendErrorAsync(id, -32602, "session/new requires cwd.", cancellationToken);

		var rootPath = Path.GetFullPath(cwd);

		if (!Directory.Exists(rootPath))
			return SendErrorAsync(id, -32602, $"Workspace does not exist: {rootPath}", cancellationToken);

		var workspace = new ProjectWorkspace(rootPath, new DirectoryInfo(rootPath).Name);
		var sessionId = Guid.NewGuid().ToString("N");

		_sessions[sessionId] = workspace;

		return SendResultAsync(id, new { sessionId }, cancellationToken);
    }

    private Task HandlePromptAsync(
        JsonElement id, JsonElement parameters, CancellationToken stoppingToken)
    {
        var sessionId = parameters.GetProperty("sessionId").GetString();

		if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var workspace))
			return SendErrorAsync(id, -32602, "Unknown ACP session.", stoppingToken);

        var message = ExtractTextPrompt(parameters);

        if (string.IsNullOrWhiteSpace(message))
            return SendErrorAsync(id, -32602, "The prompt contains no text.", stoppingToken);

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        if (!_activePrompts.TryAdd(sessionId, cancellation))
        {
            cancellation.Dispose();

            return SendErrorAsync(id, -32000, "This session already has an active prompt.", stoppingToken);
        }

        _ = RunPromptAsync(id.Clone(), sessionId, workspace, message, cancellation);

        return Task.CompletedTask;
    }

	private async Task RunPromptAsync(
		JsonElement requestId,
		string sessionId,
		ProjectWorkspace workspace,
		string message,
		CancellationTokenSource cancellation)
    {
        try
        {
            var request = new AgentRequest(message, _settings.Mode, workspace, sessionId);

            await foreach (var agentEvent in _agent.RunAsync(request, cancellation.Token))
            {
                if (agentEvent is not AgentTextDelta text)
                    continue;

                var update = new
                {
                    sessionId,
                    update = new
                    {
                        sessionUpdate = "agent_message_chunk",
                        content = new
                        {
                            type = "text",
                            text = text.Text
                        }
                    }
                };

                await SendNotificationAsync("session/update", update, cancellation.Token);
            }

            await SendResultAsync(requestId, new { stopReason = "end_turn" }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await SendResultAsync(requestId, new { stopReason = "cancelled" }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await SendErrorAsync(requestId, -32603, exception.Message, CancellationToken.None);
        }
        finally
        {
            if (_activePrompts.TryRemove(sessionId, out var activePrompt))
                activePrompt.Dispose();
        }
    }

    private void HandleCancel(JsonElement parameters)
    {
        var sessionId = parameters.GetProperty("sessionId").GetString();

        if (sessionId is not null && _activePrompts.TryGetValue(sessionId, out var cancellation))
            cancellation.Cancel();
    }

    private static string ExtractTextPrompt(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("prompt", out var prompt))
            return string.Empty;

        var parts = new List<string>();

        foreach (var block in prompt.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var type) || type.GetString() != "text")
                continue;

            if (block.TryGetProperty("text", out var text))
                parts.Add(text.GetString() ?? string.Empty);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private Task SendResultAsync(JsonElement id, object result, CancellationToken cancellationToken)
    {
        return SendAsync(new
        {
            jsonrpc = "2.0",
            id,
            result
        }, cancellationToken);
    }

    private Task SendErrorAsync(
        JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        return SendAsync(new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message
            }
        }, cancellationToken);
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        return SendAsync(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        }, cancellationToken);
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);

        await _writeLock.WaitAsync(cancellationToken);

        try
        {
            await Console.Out.WriteLineAsync(json);
            await Console.Out.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
