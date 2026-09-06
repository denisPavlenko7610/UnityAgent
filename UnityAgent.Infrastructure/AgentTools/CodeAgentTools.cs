using System.ComponentModel;
using System.Diagnostics;
using UnityAgent.Core.Code;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.AgentTools;

public sealed class CodeAgentTools
{
    private readonly ICodeIntelligence _code;
    private readonly IAgentTrace _trace;
    private readonly ProjectWorkspace _workspace;
    private readonly ToolCallBudget _budget;

    internal CodeAgentTools(
        ICodeIntelligence code,
        IAgentTrace trace,
        ProjectWorkspace workspace,
        ToolCallBudget budget)
    {
        _code = code;
        _trace = trace;
        _workspace = workspace;
        _budget = budget;
    }

    [Description("Read a small relevant range of code from a project file.")]
    public Task<string> ReadCodeAsync(
        [Description("Project-relative or Rider-returned file path.")] string filePath,
        [Description("1-based first line to read.")] int startLine,
        [Description("Number of lines to read.")] int lineCount,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"read_code:{filePath}:{startLine}:{lineCount}",
            "read_code",
            () => _code.ReadCodeAsync(_workspace, filePath, startLine, lineCount, cancellationToken));
    }

    [Description("Analyze callers or callees of an exact callable symbol using Rider call hierarchy.")]
    public Task<string> AnalyzeCallsAsync(
        [Description("Exact fully qualified callable symbol.")] string symbolFqn,
        [Description("Incoming for callers, Outgoing for callees.")] CallDirection direction,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"analyze_calls:{symbolFqn}:{direction}",
            "analyze_calls",
            () => _code.AnalyzeCallsAsync(_workspace, symbolFqn, direction, cancellationToken));
    }

    [Description("Get Rider code-analysis errors and warnings for a project file.")]
    public Task<string> GetFileProblemsAsync(
        [Description("Project-relative file path.")] string filePath,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"get_file_problems:{filePath}",
            "get_file_problems",
            () => _code.GetFileProblemsAsync(_workspace, filePath, cancellationToken));
    }

    private async Task<string> ExecuteAsync(string key, string toolName, Func<Task<string>> action)
    {
        var blocked = _budget.TryBegin(key);

        if (blocked is not null)
            return blocked;

        var started = Stopwatch.GetTimestamp();

        try
        {
            return await action();
        }
        finally
        {
            _trace.ToolCompleted(toolName, Stopwatch.GetElapsedTime(started));
        }
    }
}
