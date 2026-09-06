using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Ide;
using UnityAgent.Core.Runtime;
using UnityAgent.Core.Workspace;
using UnityAgent.Host.Hosting;
using UnityAgent.Infrastructure.Models;
using UnityAgent.Infrastructure.Rider;
using UnityAgent.Infrastructure.Workspace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddOptions<AgentSettings>()
	.Bind(builder.Configuration.GetSection(AgentSettings.SectionName));

builder.Services
	.AddOptions<LocalModelSettings>()
	.Bind(builder.Configuration.GetSection(LocalModelSettings.SectionName))
	.Validate(
		settings => !string.IsNullOrWhiteSpace(settings.ModelId),
		"LocalModel:ModelId must be configured."
	)
	.Validate(
		settings => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _),
		"LocalModel:Endpoint must be a valid URI."
	)
	.ValidateOnStart();

builder.Services
	.AddOptions<WorkspaceSettings>()
	.Bind(builder.Configuration.GetSection(WorkspaceSettings.SectionName))
	.Validate(
		settings => !string.IsNullOrWhiteSpace(settings.ProjectPath),
		"Workspace:ProjectPath must be configured."
	)
	.ValidateOnStart();

builder.Services
	.AddOptions<RiderMcpSettings>()
	.Bind(builder.Configuration.GetSection(RiderMcpSettings.SectionName))
	.Validate(
		settings =>
			Uri.TryCreate(
				settings.Endpoint,
				UriKind.Absolute,
				out _
			),
		"Rider:Endpoint must be a valid URI."
	)
	.ValidateOnStart();

builder.Services.AddSingleton<IIdeBridge, RiderMcpBridge>();
builder.Services.AddSingleton<IWorkspaceProvider, ConfiguredWorkspaceProvider>();
builder.Services.AddSingleton<IAgentRuntime, AgentFrameworkRuntime>();
builder.Services.AddHostedService<AgentWorker>();

using var host = builder.Build();

await host.RunAsync();
