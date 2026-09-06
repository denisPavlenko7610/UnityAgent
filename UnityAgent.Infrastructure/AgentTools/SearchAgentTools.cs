using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using UnityAgent.Core.Code;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Indexing;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.AgentTools;

public sealed class SearchAgentTools
{
    private const int SearchResultLimit = 12;

    private readonly ICodeIntelligence _code;
    private readonly IProjectIndex _projectIndex;
    private readonly IAgentTrace _trace;
    private readonly ProjectWorkspace _workspace;
    private readonly ToolCallBudget _budget;

    internal SearchAgentTools(
        ICodeIntelligence code,
        IProjectIndex projectIndex,
        IAgentTrace trace,
        ProjectWorkspace workspace,
        ToolCallBudget budget)
    {
        _code = code;
        _projectIndex = projectIndex;
        _trace = trace;
        _workspace = workspace;
        _budget = budget;
    }

    [Description("Find C# classes, methods, properties or fields by symbol name.")]
    public Task<string> FindSymbolAsync(
        [Description("Symbol name or fragment, for example InventoryService or AddItem.")] string query,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"find_symbol:{query}",
            "find_symbol",
            () => _code.SearchSymbolAsync(_workspace, query, cancellationToken));
    }

    [Description("Find project files by glob pattern, for example **/*Settings.cs.")]
    public Task<string> FindFileAsync(
        [Description("Project-relative glob pattern.")] string pattern,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"find_file:{pattern}",
            "find_file",
            () => _code.SearchFileAsync(_workspace, pattern, cancellationToken));
    }

    [Description(
        "Find text, strings, comments or configuration keys in the project. " +
        "Prefer find_symbol for named C# declarations.")]
    public Task<string> FindTextAsync(
        [Description("Text to search for.")] string text,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            $"find_text:{text}",
            "find_text",
            () => FindTextCoreAsync(text, cancellationToken));
    }

    private async Task<string> FindTextCoreAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var localResults = await _projectIndex.SearchTextAsync(
            _workspace,
            text,
            SearchResultLimit,
            cancellationToken);

        if (localResults.Count > 0)
            return FormatLocalResults(localResults);

        return await _code.SearchTextAsync(_workspace, text, cancellationToken);
    }

    private static string FormatLocalResults(IReadOnlyList<ProjectTextSearchResult> results)
    {
        var builder = new StringBuilder();

        foreach (var result in results)
        {
            builder.AppendLine(result.Path);
            builder.AppendLine(result.Snippet);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string> ExecuteAsync(
        string key,
        string toolName,
        Func<Task<string>> action)
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
