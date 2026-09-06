using System.Collections.Concurrent;
using System.Text;
using UnityAgent.Core.Memory;

namespace UnityAgent.Infrastructure.Memory;

public sealed class InMemorySessionMemory : ISessionMemory
{
	private const int MaximumExchanges = 3;
	private const int MaximumCharacters = 6000;

	private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

	public string GetContext(string? sessionId)
	{
		if (sessionId is null || !_sessions.TryGetValue(sessionId, out var session))
			return string.Empty;

		lock (session.Gate)
		{
			var builder = new StringBuilder();

			foreach (var exchange in session.Exchanges)
			{
				builder.AppendLine("User:");
				builder.AppendLine(exchange.User);
				builder.AppendLine();

				builder.AppendLine("Assistant:");
				builder.AppendLine(exchange.Assistant);
				builder.AppendLine();
			}

			var result = builder.ToString();

			return result.Length <= MaximumCharacters
				? result
				: result[^MaximumCharacters..];
		}
	}

	public void AddExchange(string? sessionId, string userMessage, string assistantMessage)
	{
		if (sessionId is null)
			return;

		var session = _sessions.GetOrAdd(sessionId, _ => new SessionState());

		lock (session.Gate)
		{
			session.Exchanges.Add(new Exchange(userMessage, assistantMessage));

			while (session.Exchanges.Count > MaximumExchanges)
				session.Exchanges.RemoveAt(0);
		}
	}

	public void Clear(string sessionId)
	{
		_sessions.TryRemove(sessionId, out _);
	}

	private sealed class SessionState
	{
		public object Gate { get; } = new();

		public List<Exchange> Exchanges { get; } = [];
	}

	private sealed record Exchange(string User, string Assistant);
}
