namespace UnityAgent.Core.Agent;

public sealed class AgentSettings
{
	public const string SectionName = "Agent";

	public AgentMode Mode { get; set; } = AgentMode.Explain;

	public int MaximumContextTokens { get; set; } = 8192;

	public int MaximumToolSteps { get; set; } = 8;
}
