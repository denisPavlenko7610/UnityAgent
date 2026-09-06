using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Code;
using UnityAgent.Core.Context;
using UnityAgent.Core.Diagnostics;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Memory;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;
using UnityAgent.Host.Acp;
using UnityAgent.Host.Diagnostics;
using UnityAgent.Host.Hosting;
using UnityAgent.Infrastructure.Context;
using UnityAgent.Infrastructure.Memory;
using UnityAgent.Infrastructure.Models;
using UnityAgent.Infrastructure.Rider;
using UnityAgent.Infrastructure.Workspace;

var acpMode = args.Any(arg => arg.Equals("--acp", StringComparison.OrdinalIgnoreCase));

var hostArgs = args
	.Where(arg => !arg.Equals("--acp", StringComparison.OrdinalIgnoreCase))
	.ToArray();

var hostSettings = new HostApplicationBuilderSettings
{
	Args = hostArgs,
	ContentRootPath = AppContext.BaseDirectory
};

var builder = Host.CreateApplicationBuilder(hostSettings);

if (acpMode)
	builder.Logging.ClearProviders();

builder.Services
	.AddOptions<AgentSettings>()
	.Bind(builder.Configuration.GetSection(AgentSettings.SectionName));

builder.Services
	.AddOptions<LocalModelSettings>()
	.Bind(builder.Configuration.GetSection(LocalModelSettings.SectionName))
	.Validate(settings => Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out _),
		"LocalModel:ServerUrl must be a valid URI.")
	.ValidateOnStart();

if (!acpMode)
{
	builder.Services
		.AddOptions<WorkspaceSettings>()
		.Bind(builder.Configuration.GetSection(WorkspaceSettings.SectionName))
		.Validate(settings => !string.IsNullOrWhiteSpace(settings.ProjectPath),
			"Workspace:ProjectPath must be configured.")
		.ValidateOnStart();

	builder.Services.AddSingleton<IWorkspaceProvider, ConfiguredWorkspaceProvider>();
}

builder.Services
	.AddOptions<RiderMcpSettings>()
	.Bind(builder.Configuration.GetSection(RiderMcpSettings.SectionName))
	.Validate(settings => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _),
		"Rider:Endpoint must be a valid URI.")
	.ValidateOnStart();

if (acpMode)
	builder.Services.AddSingleton<IAgentTrace, StderrAgentTrace>();
else
	builder.Services.AddSingleton<IAgentTrace, ConsoleAgentTrace>();

builder.Services.AddSingleton<IContextEngine, ContextEngine>();
builder.Services.AddSingleton<LmStudioModelResolver>();
builder.Services.AddSingleton<IIdeBridge, RiderMcpBridge>();
builder.Services.AddSingleton<ICodeIntelligence, RiderCodeIntelligence>();
builder.Services.AddSingleton<IAgentRuntime, AgentFrameworkRuntime>();
builder.Services.AddSingleton<IContextEngine, ContextEngine>();
builder.Services.AddSingleton<ICodeModification, RiderCodeModification>();
builder.Services.AddSingleton<ICodeVerification, RiderCodeVerification>();
builder.Services.AddSingleton<ISessionMemory, InMemorySessionMemory>();

if (acpMode)
	builder.Services.AddHostedService<AcpAgentWorker>();
else
	builder.Services.AddHostedService<AgentWorker>();

using var host = builder.Build();

await host.RunAsync();
