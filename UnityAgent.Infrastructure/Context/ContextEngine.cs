using System.Text;
using UnityAgent.Core.Code;
using UnityAgent.Core.Context;
using UnityAgent.Core.Memory;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Infrastructure.Context;

public sealed class ContextEngine : IContextEngine
{
    private const int MaximumResources = 3;
    private const int MaximumFileLines = 160;

	private readonly ISessionMemory _sessionMemory;

    private readonly ICodeIntelligence _code;

    public ContextEngine(ICodeIntelligence code, ISessionMemory sessionMemory)
	{
		_code = code;
		_sessionMemory = sessionMemory;
	}

    public async Task<AgentContext> BuildAsync(AgentRequest request, CancellationToken cancellationToken)
    {
		var builder = new StringBuilder();
		var history = _sessionMemory.GetContext(request.SessionId);

		if (!string.IsNullOrWhiteSpace(history))
		{
			builder.AppendLine("Recent conversation:");
			builder.AppendLine(history);
			builder.AppendLine();
		}

        builder.AppendLine("User request:");
        builder.AppendLine(request.Message);

        if (request.Resources is not { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("IDE context: none supplied by the client.");

            return new AgentContext(builder.ToString());
        }

        builder.AppendLine();
        builder.AppendLine("IDE context supplied by Rider:");

        foreach (var resource in request.Resources.Take(MaximumResources))
            await AppendResourceAsync(builder, request, resource, cancellationToken);

        return new AgentContext(builder.ToString());
    }

    private async Task AppendResourceAsync(
        StringBuilder builder,
        AgentRequest request,
        AgentPromptResource resource,
        CancellationToken cancellationToken)
    {
        builder.AppendLine();
        builder.AppendLine($"Resource: {resource.Name ?? resource.Uri}");

        if (!string.IsNullOrWhiteSpace(resource.Text))
        {
            AppendCode(builder, resource.Text);
            return;
        }

        if (!TryGetFilePath(resource.Uri, out var filePath))
            return;

        var code = await _code.ReadCodeAsync(
            request.Workspace,
            filePath,
            1,
            MaximumFileLines,
            cancellationToken);

        AppendCode(builder, code);
    }

    private static void AppendCode(StringBuilder builder, string code)
    {
        builder.AppendLine("<code>");
        builder.AppendLine(code);
        builder.AppendLine("</code>");
    }

    private static bool TryGetFilePath(string resourceUri, out string filePath)
    {
        filePath = string.Empty;

        if (!Uri.TryCreate(resourceUri, UriKind.Absolute, out var uri) || !uri.IsFile)
            return false;

        filePath = uri.LocalPath;

        return true;
    }
}
