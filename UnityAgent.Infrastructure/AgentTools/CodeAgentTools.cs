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
	private ProjectWorkspace _workspace;

	public CodeAgentTools(ICodeIntelligence code, IAgentTrace trace, ProjectWorkspace workspace)
	{
		_code = code;
		_trace = trace;
		_workspace = workspace;
	}

	[Description("Find C# classes, methods, properties or fields in the current project by symbol name.")]
	public async Task<string> SearchSymbolAsync(
		[Description("Symbol name or fragment, for example InventoryService or AddItem.")] string query,
		CancellationToken cancellationToken)
	{
		var started = Stopwatch.GetTimestamp();

		try
		{
			return await _code.SearchSymbolAsync(_workspace, query, cancellationToken);
		}
		finally
		{
			_trace.ToolCompleted("search_symbol", Stopwatch.GetElapsedTime(started));
		}
	}

	[Description("Read a small relevant range of code from a project file.")]
	public Task<string> ReadCodeAsync(
		[Description("Project-relative file path returned by another code tool.")] string filePath,
		[Description("1-based first line to read.")] int startLine,
		[Description("Number of lines to read.")] int lineCount,
		CancellationToken cancellationToken)
	{
		return _code.ReadCodeAsync(_workspace, filePath, startLine, lineCount, cancellationToken);
	}

	[Description("Analyze callers or callees of an exact callable symbol using Rider call hierarchy.")]
	public Task<string> AnalyzeCallsAsync(
		[Description("Exact fully qualified callable name returned by symbol search.")] string symbolFqn,
		[Description("Incoming for callers, Outgoing for functions called by the symbol.")] CallDirection direction,
		CancellationToken cancellationToken)
	{
		return _code.AnalyzeCallsAsync(_workspace, symbolFqn, direction, cancellationToken);
	}

	[Description("Get Rider code-analysis errors and warnings for a project file.")]
	public Task<string> GetFileProblemsAsync(
		[Description("Project-relative file path.")] string filePath,
		CancellationToken cancellationToken)
	{
		return _code.GetFileProblemsAsync(_workspace, filePath, cancellationToken);
	}
}
