using System.Diagnostics;
using System.Text.Json;
using UnityAgent.Core.Code;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Rider;

public sealed class RiderCodeVerification : ICodeVerification
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(30);

    private readonly IIdeBridge _ide;

    public RiderCodeVerification(IIdeBridge ide)
    {
        _ide = ide;
    }

    public async Task<string> BuildAsync(ProjectWorkspace workspace, CancellationToken cancellationToken)
    {
        var startResult = await _ide.CallToolAsync(
            workspace,
            "build_solution_start",
            new Dictionary<string, object?> { ["rebuild"] = false },
            cancellationToken);

        var sessionId = TryGetString(startResult, "sessionId");
        var started = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(started) < BuildTimeout)
        {
            await Task.Delay(PollInterval, cancellationToken);

            IReadOnlyDictionary<string, object?>? arguments = sessionId is null
                ? null
                : new Dictionary<string, object?> { ["sessionId"] = sessionId };

            var state = await _ide.CallToolAsync(
                workspace,
                "build_solution_state",
                arguments,
                cancellationToken);

            if (IsFinished(state))
                return state;
        }

        return "Build verification timed out.";
    }

    private static bool IsFinished(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("state", out var state))
                return false;

            return state.GetString() is "Completed" or "Cancelled" or "NotFound";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryGetString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.TryGetProperty(propertyName, out var property)
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
