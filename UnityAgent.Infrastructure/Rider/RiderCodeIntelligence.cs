using UnityAgent.Core.Code;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Rider;

public sealed class RiderCodeIntelligence : ICodeIntelligence
{
    private const int SearchResultLimit = 8;
    private const int MaximumReadLines = 160;

    private readonly IIdeBridge _ide;

    public RiderCodeIntelligence(IIdeBridge ide)
	{
		_ide = ide;
	}

	public Task<string> SearchFileAsync(
		ProjectWorkspace workspace, string pattern, CancellationToken cancellationToken)
	{
		var arguments = new Dictionary<string, object?>
		{
			["q"] = pattern,
			["includeExcluded"] = false,
			["limit"] = 12
		};

		return _ide.CallToolAsync(workspace, "search_file", arguments, cancellationToken);
	}

	public Task<string> SearchTextAsync(
		ProjectWorkspace workspace, string text, CancellationToken cancellationToken)
	{
		var arguments = new Dictionary<string, object?>
		{
			["q"] = text,
			["limit"] = 12
		};

		return _ide.CallToolAsync(workspace, "search_text", arguments, cancellationToken);
	}

	public Task<string> SearchSymbolAsync(ProjectWorkspace workspace, string query, CancellationToken cancellationToken)
	{
		var arguments = new Dictionary<string, object?>
		{
			["q"] = query,
			["include_external"] = false,
			["limit"] = SearchResultLimit
		};

		return _ide.CallToolAsync(workspace, "search_symbol", arguments, cancellationToken);
	}

    public Task<string> ReadCodeAsync(ProjectWorkspace workspace,
        string filePath, int startLine, int lineCount, CancellationToken cancellationToken)
    {
        var safeLineCount = Math.Clamp(lineCount, 1, MaximumReadLines);

        var arguments = new Dictionary<string, object?>
        {
            ["file_path"] = filePath,
            ["offset"] = Math.Max(startLine, 1),
            ["limit"] = safeLineCount
        };

        return _ide.CallToolAsync(workspace,"read_file", arguments, cancellationToken);
    }

    public Task<string> AnalyzeCallsAsync(ProjectWorkspace workspace,
        string symbolFqn, CallDirection direction, CancellationToken cancellationToken)
    {
        var analysisKind = direction switch
        {
            CallDirection.Incoming => "INCOMING_CALLS",
            CallDirection.Outgoing => "OUTGOING_CALLS",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        var arguments = new Dictionary<string, object?>
        {
            ["symbolFqn"] = symbolFqn,
            ["analysisKind"] = analysisKind,
            ["depth"] = 2,
            ["maxChildren"] = 20,
            ["maxNodes"] = 100
        };

        return _ide.CallToolAsync(workspace,"analyze_calls", arguments, cancellationToken);
    }

    public Task<string> GetFileProblemsAsync(ProjectWorkspace workspace, string filePath, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["errorsOnly"] = false,
            ["timeout"] = 5000
        };

        return _ide.CallToolAsync(workspace,"get_file_problems", arguments, cancellationToken);
    }

	private async Task<string?> TryExecuteEditorToolAsync(
		ProjectWorkspace workspace, string command, CancellationToken cancellationToken)
	{
		try
		{
			var arguments = new Dictionary<string, object?>
			{
				["command"] = command
			};

			var result = await _ide.CallToolAsync(workspace, "execute_tool", arguments, cancellationToken);

			return string.IsNullOrWhiteSpace(result)
				? null
				: result.Trim();
		}
		catch (Exception)
		{
			return null;
		}
	}
}
