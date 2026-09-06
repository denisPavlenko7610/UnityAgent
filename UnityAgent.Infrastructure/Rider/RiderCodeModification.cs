using UnityAgent.Core.Code;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Rider;

public sealed class RiderCodeModification : ICodeModification
{
	private readonly IIdeBridge _ide;

	public RiderCodeModification(IIdeBridge ide)
	{
		_ide = ide;
	}

	public Task<string> ApplyPatchAsync(
		ProjectWorkspace workspace, string patch, CancellationToken cancellationToken)
	{
		var arguments = new Dictionary<string, object?>
		{
			["input"] = patch
		};

		return _ide.CallToolAsync(workspace, "apply_patch", arguments, cancellationToken);
	}
}
