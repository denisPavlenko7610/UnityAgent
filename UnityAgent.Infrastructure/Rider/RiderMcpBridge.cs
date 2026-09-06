using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Workspace;

namespace UnityAgent.Infrastructure.Rider;

public sealed class RiderMcpBridge : IIdeBridge, IAsyncDisposable
{
    private readonly RiderMcpSettings _settings;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

	private const string RootFolderArgument = "rootFolder";

    private McpClient? _client;

    public RiderMcpBridge(IOptions<RiderMcpSettings> settings)
    {
        _settings = settings.Value;
    }

	public async Task<string> CallToolAsync(
		ProjectWorkspace workspace,
		string toolName,
		IReadOnlyDictionary<string, object?>? arguments,
		CancellationToken cancellationToken)
	{
		var client = await GetClientAsync(cancellationToken);

		var toolArguments = arguments is null
			? new Dictionary<string, object?>()
			: new Dictionary<string, object?>(arguments);

		toolArguments["rootFolder"] = workspace.RootPath;

		var result = await client.CallToolAsync(
			toolName,
			toolArguments,
			cancellationToken: cancellationToken);

		var textBlocks = result.Content
			.OfType<TextContentBlock>()
			.Select(block => block.Text);

		return string.Join(Environment.NewLine, textBlocks);
	}

    public async Task<IReadOnlyList<IdeToolInfo>> GetAvailableToolsAsync(
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

		return tools
			.Select(tool => new IdeToolInfo(tool.Name, tool.Description, tool.JsonSchema.GetRawText()))
			.ToArray();
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
            return _client;

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_client is not null)
                return _client;

            var options = new HttpClientTransportOptions
            {
                Endpoint = new Uri(_settings.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
            };

            var transport = new HttpClientTransport(options);

            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            return _client;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();

        _connectionLock.Dispose();
    }
}
