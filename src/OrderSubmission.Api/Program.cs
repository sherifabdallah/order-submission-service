using OrderSubmission.Api;
using OrderSubmission.Application;
using OrderSubmission.Infrastructure;
using OrderSubmission.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddPresentation();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UsePresentation();

await app.RunAsync();
