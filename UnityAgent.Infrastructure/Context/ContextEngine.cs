using System.Text.Json;
using UnityAgent.Core.Code;
using UnityAgent.Core.Context;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Infrastructure.Context;

public sealed class ContextEngine : IContextEngine
{
    private const int AutomaticReadLines = 160;

    private readonly ICodeIntelligence _code;

    public ContextEngine(ICodeIntelligence code)
    {
        _code = code;
    }

    public async Task<AgentContext> BuildAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!NeedsEditorContext(request.Message))
            return new AgentContext(request.Message);

        var rawOpenFiles = await _code.GetOpenFilesAsync(request.Workspace, cancellationToken);
        var openFiles = TryParseOpenFiles(rawOpenFiles);

        if (openFiles.Count == 1)
        {
            var filePath = openFiles[0];
            var code = await _code.ReadCodeAsync(
                request.Workspace,
                filePath,
                1,
                AutomaticReadLines,
                cancellationToken);

            return new AgentContext(BuildSingleFilePrompt(request.Message, filePath, code));
        }

        return new AgentContext(BuildAmbiguousEditorPrompt(request.Message, rawOpenFiles));
    }

    private static bool NeedsEditorContext(string message)
    {
        var text = message.ToLowerInvariant();

        return text.Contains("этот код") ||
               text.Contains("этот файл") ||
               text.Contains("текущий код") ||
               text.Contains("текущий файл") ||
               text.Contains("здесь") ||
               text.Contains("this code") ||
               text.Contains("this file") ||
               text.Contains("current file");
    }

    private static IReadOnlyList<string> TryParseOpenFiles(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return ReadStringArray(root);

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("openFiles", out var openFiles) &&
                openFiles.ValueKind == JsonValueKind.Array)
            {
                return ReadStringArray(openFiles);
            }
        }
        catch (JsonException)
        {
            // Rider may return plain text depending on MCP version.
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array)
    {
        var result = new List<string>();

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var path = item.GetString();

            if (!string.IsNullOrWhiteSpace(path))
                result.Add(path);
        }

        return result;
    }

    private static string BuildSingleFilePrompt(string userMessage, string filePath, string code)
    {
        return $"""
                User request:
                {userMessage}

                The IDE has exactly one open file, so it is the editor-relative target of the request.

                File:
                {filePath}

                Actual code retrieved from Rider:
                <code>
                {code}
                </code>

                Base the answer on this code. Do not search for unrelated symbols unless the code itself requires it.
                """;
    }

    private static string BuildAmbiguousEditorPrompt(string userMessage, string rawOpenFiles)
    {
        return $"""
                User request:
                {userMessage}

                The user referred to editor-relative code, but the IDE did not provide one unambiguous active file.

                Rider open-file information:
                {rawOpenFiles}

                Do not guess the file, class, domain, or project feature.
                Do not perform speculative symbol searches.
                Explain that the active code could not be determined unambiguously.
                """;
    }
}
