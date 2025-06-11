using dissertation_backend.Middlewares;
using dissertation_backend.Services.Implementations;
using dissertation_backend.Services.Interfaces;
using dissertation_backend.Workers;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

// Add services
services.AddControllers();
builder.Services.AddHttpClient();
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

// Add webhook services
builder.Services.AddScoped<IGitHubRepositoryService, GitHubRepositoryService>();
builder.Services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
services.AddSingleton<IWebhookProcessingQueue, WebhookProcessingQueue>();
services.AddHostedService<WebhookBackgroundService>();

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

app.UseMiddleware<GitHubSignatureValidationMiddleware>();
app.UseMiddleware<GitHubEventFilterMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();


app.Run();
