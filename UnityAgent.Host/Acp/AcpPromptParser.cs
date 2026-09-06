using System.Text.Json;
using UnityAgent.Core.Runtime;

namespace UnityAgent.Host.Acp;

internal static class AcpPromptParser
{
    public static AcpPrompt Parse(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.Array)
            return new AcpPrompt(string.Empty, Array.Empty<AgentPromptResource>());

        var textParts = new List<string>();
        var resources = new List<AgentPromptResource>();

        foreach (var block in prompt.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeElement))
                continue;

            switch (typeElement.GetString())
            {
                case "text":
                    ReadText(block, textParts);
                    break;

                case "resource_link":
                    ReadResourceLink(block, resources);
                    break;

                case "resource":
                    ReadEmbeddedResource(block, resources);
                    break;
            }
        }

        return new AcpPrompt(string.Join(Environment.NewLine, textParts), resources);
    }

    private static void ReadText(JsonElement block, ICollection<string> textParts)
    {
        if (!block.TryGetProperty("text", out var textElement))
            return;

        var text = textElement.GetString();

        if (!string.IsNullOrWhiteSpace(text))
            textParts.Add(text);
    }

    private static void ReadResourceLink(JsonElement block, ICollection<AgentPromptResource> resources)
    {
        if (!block.TryGetProperty("uri", out var uriElement))
            return;

        var uri = uriElement.GetString();

        if (string.IsNullOrWhiteSpace(uri))
            return;

        resources.Add(new AgentPromptResource(
            uri,
            GetOptionalString(block, "name"),
            GetOptionalString(block, "mimeType")));
    }

    private static void ReadEmbeddedResource(JsonElement block, ICollection<AgentPromptResource> resources)
    {
        if (!block.TryGetProperty("resource", out var resource))
            return;

        if (!resource.TryGetProperty("uri", out var uriElement))
            return;

        var uri = uriElement.GetString();

        if (string.IsNullOrWhiteSpace(uri))
            return;

        resources.Add(new AgentPromptResource(
            uri,
            MimeType: GetOptionalString(resource, "mimeType"),
            Text: GetOptionalString(resource, "text")));
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }
}
