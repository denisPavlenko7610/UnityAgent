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
    private readonly int _maximumToolSteps;

    private readonly HashSet<string> _executedCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _toolGate = new();

    private int _toolSteps;

    public CodeAgentTools(ICodeIntelligence code, IAgentTrace trace, ProjectWorkspace workspace, int maximumToolSteps)
    {
        _code = code;
        _trace = trace;
        _workspace = workspace;
        _maximumToolSteps = maximumToolSteps;
    }

	[Description("Find C# classes, methods, properties or fields in the current project by symbol name.")]
    public Task<string> SearchSymbolAsync(
        [Description("Symbol name or fragment, for example InventoryService or AddItem.")] string query,
        CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(
            $"search_symbol:{query}",
            "search_symbol",
            () => _code.SearchSymbolAsync(_workspace, query, cancellationToken));
    }

    [Description("Read a small relevant range of code from a project file.")]
    public Task<string> ReadCodeAsync(
        [Description("Project-relative file path returned by another code tool.")] string filePath,
        [Description("1-based first line to read.")] int startLine,
        [Description("Number of lines to read.")] int lineCount,
        CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(
            $"read_code:{filePath}:{startLine}:{lineCount}",
            "read_code",
            () => _code.ReadCodeAsync(_workspace, filePath, startLine, lineCount, cancellationToken));
    }

    [Description("Analyze callers or callees of an exact callable symbol using Rider call hierarchy.")]
    public Task<string> AnalyzeCallsAsync(
        [Description("Exact fully qualified callable name returned by symbol search.")] string symbolFqn,
        [Description("Incoming for callers, Outgoing for functions called by the symbol.")] CallDirection direction,
        CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(
            $"analyze_calls:{symbolFqn}:{direction}",
            "analyze_calls",
            () => _code.AnalyzeCallsAsync(_workspace, symbolFqn, direction, cancellationToken));
    }

    [Description("Get Rider code-analysis errors and warnings for a project file.")]
    public Task<string> GetFileProblemsAsync(
        [Description("Project-relative file path.")] string filePath,
        CancellationToken cancellationToken)
    {
        return ExecuteToolAsync(
            $"get_file_problems:{filePath}",
            "get_file_problems",
            () => _code.GetFileProblemsAsync(_workspace, filePath, cancellationToken));
    }

    private async Task<string> ExecuteToolAsync(string key, string toolName, Func<Task<string>> action)
    {
        var blocked = TryBeginToolCall(key);

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

    private string? TryBeginToolCall(string key)
    {
        lock (_toolGate)
        {
            if (_executedCalls.Contains(key))
                return "Duplicate tool call blocked. Reuse the result from the previous identical call.";

            if (_toolSteps >= _maximumToolSteps)
                return "Tool budget exhausted. Answer using the evidence already collected.";

            _executedCalls.Add(key);
            _toolSteps++;

            return null;
        }
    }
}
