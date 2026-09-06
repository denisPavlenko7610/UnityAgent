using UnityAgent.Core.Workspace;

namespace UnityAgent.Core.Code;

public interface ICodeIntelligence
{
	Task<string> GetOpenFilesAsync(ProjectWorkspace workspace, CancellationToken cancellationToken);

	Task<string> SearchSymbolAsync(ProjectWorkspace workspace, string query, CancellationToken cancellationToken);

	Task<string> ReadCodeAsync(
		ProjectWorkspace workspace,
		string filePath,
		int startLine,
		int lineCount,
		CancellationToken cancellationToken);

	Task<string> AnalyzeCallsAsync(
		ProjectWorkspace workspace,
		string symbolFqn,
		CallDirection direction,
		CancellationToken cancellationToken);

	Task<string> GetFileProblemsAsync(
		ProjectWorkspace workspace, string filePath, CancellationToken cancellationToken);
}
