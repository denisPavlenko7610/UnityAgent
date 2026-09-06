namespace UnityAgent.Infrastructure.Models;

public sealed class LocalModelSettings
{
	public const string SectionName = "LocalModel";

	public string ServerUrl { get; set; } = "http://127.0.0.1:1234";
}
