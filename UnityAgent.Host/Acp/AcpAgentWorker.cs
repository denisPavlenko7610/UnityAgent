using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Indexing;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Host.Acp;

internal sealed class AcpAgentWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAgentRuntime _agent;
    private readonly AgentSettings _settings;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IProjectIndex _projectIndex;

    private readonly ConcurrentDictionary<string, AcpSessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

	private int _code = -32602;

	public AcpAgentWorker(
        IAgentRuntime agent,
        IOptions<AgentSettings> settings,
        IHostApplicationLifetime applicationLifetime,
        IProjectIndex projectIndex)
    {
        _agent = agent;
        _settings = settings.Value;
        _applicationLifetime = applicationLifetime;
        _projectIndex = projectIndex;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(stoppingToken);

                if (line is null)
                    break;

                await HandleMessageAsync(line, stoppingToken);
            }
        }
        finally
        {
            CancelActivePrompts();

            // BackgroundService finishing does not automatically terminate Generic Host.
            // Rider closing ACP stdin means this ACP process is no longer needed.
            _applicationLifetime.StopApplication();
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

                case "session/set_mode":
                    await HandleSetModeAsync(id, parameters, stoppingToken);
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
            await Console.Error.WriteLineAsync($"Invalid ACP JSON: {exception.Message}");
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"ACP message handling failed: {exception}");
        }
    }

	private Task HandleInitializeAsync(JsonElement id, CancellationToken cancellationToken)
	{
		var result = new
		{
			protocolVersion = 1,
			agentCapabilities = new
			{
				loadSession = false,
				promptCapabilities = new
				{
					image = false,
					audio = false,
					embeddedContext = true
				}
			}
		};

		return SendResultAsync(id, result, cancellationToken);
	}

    private async Task HandleNewSessionAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var cwd = parameters.GetProperty("cwd").GetString();

        if (string.IsNullOrWhiteSpace(cwd))
        {
            await SendErrorAsync(id, _code, "session/new requires cwd.", cancellationToken);
            return;
        }

        var rootPath = Path.GetFullPath(cwd);

        if (!Directory.Exists(rootPath))
        {
            await SendErrorAsync(id, _code, $"Workspace does not exist: {rootPath}", cancellationToken);
            return;
        }

        var workspace = new ProjectWorkspace(rootPath, new DirectoryInfo(rootPath).Name);

        try
        {
            await _projectIndex.EnsureReadyAsync(workspace, cancellationToken);
        }
        catch (Exception exception)
        {
            await SendErrorAsync(id, -32603, $"Failed to initialize project storage: {exception.Message}", cancellationToken);
            return;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var session = new AcpSessionState(workspace, _settings.Mode);

        _sessions[sessionId] = session;

        var result = new
        {
            sessionId,

            modes = new
            {
                currentModeId = ToModeId(session.Mode),
                availableModes = new[]
                {
                    new
                    {
                        id = "explain",
                        name = "Explain",
                        description = "Explain and analyze project code without modifying files."
                    },
                    new
                    {
                        id = "code",
                        name = "Code",
                        description = "Implement and refactor project code, then verify the changes."
                    },
                    new
                    {
                        id = "debug",
                        name = "Debug",
                        description = "Investigate root causes, gather evidence, fix and verify."
                    }
                }
            }
        };

        await SendResultAsync(id, result, cancellationToken);
    }

    private Task HandleSetModeAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var sessionId = parameters.GetProperty("sessionId").GetString();
        var modeId = parameters.GetProperty("modeId").GetString();

        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
            return SendErrorAsync(id, _code, "Unknown ACP session.", cancellationToken);

        if (!TryParseMode(modeId, out var mode))
            return SendErrorAsync(id, _code, $"Unknown agent mode: {modeId}", cancellationToken);

        session.Mode = mode;

        return SendResultAsync(id, new { }, cancellationToken);
    }

    private Task HandlePromptAsync(JsonElement id, JsonElement parameters, CancellationToken stoppingToken)
    {
        var sessionId = parameters.GetProperty("sessionId").GetString();

        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
            return SendErrorAsync(id, _code, "Unknown ACP session.", stoppingToken);

		var prompt = AcpPromptParser.Parse(parameters);

		if (string.IsNullOrWhiteSpace(prompt.Text) && prompt.Resources.Count == 0)
			return SendErrorAsync(id, _code, "The prompt contains no supported content.", stoppingToken);

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        if (!_activePrompts.TryAdd(sessionId, cancellation))
        {
            cancellation.Dispose();

            return SendErrorAsync(id, -32000, "This session already has an active prompt.", stoppingToken);
        }

		_ = RunPromptAsync(id.Clone(), sessionId, session, prompt, cancellation);

        return Task.CompletedTask;
    }

	private async Task RunPromptAsync(
		JsonElement requestId,
		string sessionId,
		AcpSessionState session,
		AcpPrompt prompt,
		CancellationTokenSource cancellation)
    {
        try
        {
			var request = new AgentRequest(
				prompt.Text,
				session.Mode,
				session.Workspace,
				sessionId,
				prompt.Resources);

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
            await Console.Error.WriteLineAsync($"ACP prompt failed: {exception}");

            try
            {
                await SendErrorAsync(requestId, -32603, exception.Message, CancellationToken.None);
            }
            catch (Exception sendException)
            {
                await Console.Error.WriteLineAsync($"Failed to send ACP error: {sendException}");
            }
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

    private void CancelActivePrompts()
    {
        foreach (var cancellation in _activePrompts.Values)
            cancellation.Cancel();
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

    private Task SendErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
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

    private static string ToModeId(AgentMode mode)
    {
        return mode switch
        {
            AgentMode.Explain => "explain",
            AgentMode.Code => "code",
            AgentMode.Debug => "debug",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static bool TryParseMode(string? modeId, out AgentMode mode)
    {
        switch (modeId)
        {
            case "explain":
                mode = AgentMode.Explain;
                return true;

            case "code":
                mode = AgentMode.Code;
                return true;

            case "debug":
                mode = AgentMode.Debug;
                return true;

            default:
                mode = default;
                return false;
        }
    }
}
