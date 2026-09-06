using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnityAgent.Core.Agent;
using UnityAgent.Core.Runtime;
using UnityAgent.Host.Hosting;
using UnityAgent.Infrastructure.Models;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
	.AddOptions<AgentSettings>()
	.Bind(builder.Configuration.GetSection(AgentSettings.SectionName));

builder.Services
	.AddOptions<LocalModelSettings>()
	.Bind(builder.Configuration.GetSection(LocalModelSettings.SectionName))
	.Validate(settings => !string.IsNullOrWhiteSpace(settings.ModelId),
		"LocalModel:ModelId must be configured.")
	.Validate(settings => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _),
		"LocalModel:Endpoint must be a valid URI.")
	.ValidateOnStart();

builder.Services.AddSingleton<IAgentRuntime, AgentFrameworkRuntime>();
builder.Services.AddHostedService<AgentWorker>();

using var host = builder.Build();

await host.RunAsync();
