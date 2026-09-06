using System.ComponentModel;
using System.Diagnostics;
using UnityAgent.Core.Code;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.AgentTools;

public sealed class CodeMutationTools
{
	private readonly ICodeModification _modification;
	private readonly ICodeVerification _verification;
	private readonly IAgentTrace _trace;
	private readonly ProjectWorkspace _workspace;

	public CodeMutationTools(
		ICodeModification modification,
		ICodeVerification verification,
		IAgentTrace trace,
		ProjectWorkspace workspace)
	{
		_modification = modification;
		_verification = verification;
		_trace = trace;
		_workspace = workspace;
	}

	[Description("Apply a focused patch to project files.")]
	public Task<string> ApplyPatchAsync(
		[Description("Patch in apply_patch or unified diff format.")] string patch,
		CancellationToken cancellationToken)
	{
		return ExecuteAsync(
			"apply_patch",
			() => _modification.ApplyPatchAsync(_workspace, patch, cancellationToken));
	}

	[Description("Build the current solution and return the final verification result.")]
	public Task<string> BuildAsync(CancellationToken cancellationToken)
	{
		return ExecuteAsync(
			"build",
			() => _verification.BuildAsync(_workspace, cancellationToken));
	}

	private async Task<string> ExecuteAsync(string toolName, Func<Task<string>> action)
	{
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
