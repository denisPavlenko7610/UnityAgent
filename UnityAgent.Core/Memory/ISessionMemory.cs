namespace UnityAgent.Core.Memory;

public interface ISessionMemory
{
	string GetContext(string? sessionId);

	void AddExchange(string? sessionId, string userMessage, string assistantMessage);

	void Clear(string sessionId);
}
