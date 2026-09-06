namespace UnityAgent.Core.Runtime;

public abstract record AgentRunEvent;

public sealed record AgentTextDelta(string Text) : AgentRunEvent;
