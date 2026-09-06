namespace UnityAgent.Core.Runtime;

public sealed record AgentPromptResource(
	string Uri,
	string? Name = null,
	string? MimeType = null,
	string? Text = null);
