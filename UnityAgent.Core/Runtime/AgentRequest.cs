using UnityAgent.Core.Agent;

namespace UnityAgent.Core.Runtime;

public sealed record AgentRequest(string Message, AgentMode Mode, string? SessionId = null);
