using System.ClientModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Code;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;
using UnityAgent.Infrastructure.AgentTools;

namespace UnityAgent.Infrastructure.Models;

public sealed class AgentFrameworkRuntime : IAgentRuntime
{
    private readonly LocalModelSettings _settings;
    private readonly LmStudioModelResolver _modelResolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ICodeIntelligence _code;
    private readonly IAgentTrace _trace;

    private readonly ConcurrentDictionary<string, ChatClient> _clients = new();

    public AgentFrameworkRuntime(
        IOptions<LocalModelSettings> settings,
        LmStudioModelResolver modelResolver,
        ICodeIntelligence code,
        IAgentTrace trace,
        ILoggerFactory loggerFactory)
    {
        _settings = settings.Value;
        _modelResolver = modelResolver;
        _code = code;
        _trace = trace;
        _loggerFactory = loggerFactory;
    }

    public async IAsyncEnumerable<AgentRunEvent> RunAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var modelId = await _modelResolver.GetLoadedModelIdAsync(cancellationToken);
        var chatClient = _clients.GetOrAdd(modelId, CreateChatClient);
        var tools = CreateTools(request.Mode, request.Workspace);

        AIAgent agent = chatClient.AsAIAgent(
            instructions: AgentPolicy.Build(request.Mode),
            name: "UnityAgent",
            tools: tools,
            loggerFactory: _loggerFactory);

        await foreach (var update in agent.RunStreamingAsync(
                           request.Message,
                           cancellationToken: cancellationToken))
        {
            if (string.IsNullOrEmpty(update.Text))
                continue;

            yield return new AgentTextDelta(update.Text);
        }
    }

    private IList<AITool> CreateTools(AgentMode mode, ProjectWorkspace workspace)
    {
        var codeTools = new CodeAgentTools(_code, _trace, workspace);

        IList<AITool> readOnlyTools =
        [
            AIFunctionFactory.Create(codeTools.SearchSymbolAsync, name: "search_symbol"),
            AIFunctionFactory.Create(codeTools.ReadCodeAsync, name: "read_code"),
            AIFunctionFactory.Create(codeTools.AnalyzeCallsAsync, name: "analyze_calls"),
            AIFunctionFactory.Create(codeTools.GetFileProblemsAsync, name: "get_file_problems")
        ];

        return mode switch
        {
            AgentMode.Explain => readOnlyTools,
            AgentMode.Code => readOnlyTools,
            AgentMode.Debug => readOnlyTools,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private ChatClient CreateChatClient(string modelId)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(_settings.ServerUrl.TrimEnd('/') + "/v1")
        };

        return new ChatClient(modelId, new ApiKeyCredential("lm-studio"), options);
    }
}
