using dissertation_backend.Middlewares;
using dissertation_backend.Services.Implementations;
using dissertation_backend.Services.Interfaces;
using dissertation_backend.Workers;
using GeminiIntegration;
using GeneratedUnitTestsCompiler;
using Microsoft.Extensions.Options;
using Models.GithubModels.Configuration;
using Octokit;
using SignalRLogger;
using SignalRLogger.Hubs;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

// Add services
services.AddControllers();
builder.Services.AddHttpClient();
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

// Add webhook services
services.AddScoped<IGitHubRepositoryService, GitHubRepositoryService>();
services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
services.AddScoped<IPatchMergerService, PatchMergerService>();
services.AddScoped<IGeminiUnitTestGenerator, GeminiUnitTestGenerator>();
services.AddScoped<ITestCodeCompiler, TestCodeCompiler>();
services.AddScoped<ISignalRLoggerService, SignalRLoggerService>();
services.AddSingleton<IWebhookProcessingQueue, WebhookProcessingQueue>();
services.AddHostedService<WebhookBackgroundService>();
services.AddHttpClient<GeminiUnitTestGenerator>();
services.AddSignalR();

var gitHubToken = builder.Configuration["GitHub:Token"];

services.AddSingleton(provider =>
{
    var client = new GitHubClient(new ProductHeaderValue("DissertationBackend"))
    {
        Credentials = new Credentials(gitHubToken)
    };

    return client;
});

services.Configure<GitHub>(builder.Configuration.GetRequiredSection("GitHub"));
services.AddScoped(cfg => cfg.GetService<IOptions<GitHub>>().Value);

services.AddCors();


// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(builder => builder
  .SetIsOriginAllowed(origin => true)
  .AllowAnyMethod()
  .AllowAnyHeader()
  .AllowCredentials());

app.UseRouting();

app.UseMiddleware<GitHubSignatureValidationMiddleware>();
app.UseMiddleware<GitHubEventFilterMiddleware>();

app.MapHub<LoggingHub>("/loggingHub");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
