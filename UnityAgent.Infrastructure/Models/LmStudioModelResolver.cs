using System.Text.Json;
using Microsoft.Extensions.Options;

namespace UnityAgent.Infrastructure.Models;

public sealed class LmStudioModelResolver : IDisposable
{
	private readonly HttpClient _httpClient;

	public LmStudioModelResolver(IOptions<LocalModelSettings> settings)
	{
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(settings.Value.ServerUrl.TrimEnd('/') + "/")
		};
	}

	public async Task<string> GetLoadedModelIdAsync(CancellationToken cancellationToken)
	{
		using var response = await _httpClient.GetAsync("api/v1/models", cancellationToken);
		response.EnsureSuccessStatusCode();

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

		foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
		{
			if (!IsLlm(model))
				continue;

			if (!model.TryGetProperty("loaded_instances", out var instances) || instances.GetArrayLength() == 0)
				continue;

			var modelId = instances[0].GetProperty("id").GetString();

			if (!string.IsNullOrWhiteSpace(modelId))
				return modelId;
		}

		throw new InvalidOperationException(
			"LM Studio has no loaded LLM. Load a chat model in LM Studio before using UnityAgent.");
	}

	private static bool IsLlm(JsonElement model)
	{
		return model.TryGetProperty("type", out var type) && type.GetString() == "llm";
	}

	public void Dispose()
	{
		_httpClient.Dispose();
	}
}
