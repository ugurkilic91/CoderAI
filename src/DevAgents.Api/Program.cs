using DevAgents.Api;
using DevAgents.Api.Endpoints;
using DevAgents.Api.Hosting;
using DevAgents.Application;
using DevAgents.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddDevAgentsConfiguration();
builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSystemInfoEndpoints();
app.MapContextEndpoints();
app.MapChatEndpoints();
app.MapOpenAiCompatibleEndpoints();

app.Run();
