using System.ComponentModel;
using UnityAgent.Core.Code;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.AgentTools;

public sealed class EditorContextAgentTools
{
    private const int MaximumReadLines = 200;

    private readonly ICodeIntelligence _code;
    private readonly ProjectWorkspace _workspace;
    private readonly IReadOnlyList<AgentPromptResource>? _resources;

    public EditorContextAgentTools(
        ICodeIntelligence code,
        ProjectWorkspace workspace,
        IReadOnlyList<AgentPromptResource>? resources)
    {
        _code = code;
        _workspace = workspace;
        _resources = resources;
    }

    [Description(
        "Read the file currently supplied by the IDE for this request. " +
        "Use when the user refers to the current, open, attached, or shown code.")]
    public async Task<string> ReadCurrentFileAsync(CancellationToken cancellationToken)
    {
        var resource = FindCurrentFileResource();

        if (resource is null)
            return "No current IDE file was supplied for this request.";

        if (!string.IsNullOrWhiteSpace(resource.Text))
            return resource.Text;

        if (!TryGetFilePath(resource.Uri, out var filePath))
            return "The current IDE resource is not a readable local file.";

        return await _code.ReadCodeAsync(
            _workspace,
            filePath,
            1,
            MaximumReadLines,
            cancellationToken);
    }

    private AgentPromptResource? FindCurrentFileResource()
    {
        if (_resources is not { Count: > 0 })
            return null;

        return _resources.FirstOrDefault(IsLocalFileResource);
    }

    private static bool IsLocalFileResource(AgentPromptResource resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.Text))
            return true;

        return Uri.TryCreate(resource.Uri, UriKind.Absolute, out var uri) && uri.IsFile;
    }

    private static bool TryGetFilePath(string uriText, out string filePath)
    {
        filePath = string.Empty;

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || !uri.IsFile)
            return false;

        filePath = uri.LocalPath;

        return true;
    }
}
