namespace UnityAgent.Infrastructure.Models;

public sealed class LocalModelSettings
{
	public const string SectionName = "LocalModel";

	public string Endpoint { get; set; } = "http://127.0.0.1:1234/v1";

	public string ModelId { get; set; } = string.Empty;
}
