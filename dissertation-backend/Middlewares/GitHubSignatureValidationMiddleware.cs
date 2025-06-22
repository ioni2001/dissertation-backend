using System.Security.Cryptography;
using System.Text;

namespace dissertation_backend.Middlewares;

public class GitHubSignatureValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitHubSignatureValidationMiddleware> _logger;

    public GitHubSignatureValidationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<GitHubSignatureValidationMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/webhooks/github"))
        {
            await _next(context);
            return;
        }

        var secret = _configuration["GitHub:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogError("GitHub webhook secret not configured");

            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Server configuration error");
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader))
        {
            _logger.LogWarning("Missing X-Hub-Signature-256 header");

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing signature");
            return;
        }

        var signature = signatureHeader.FirstOrDefault();
        if (string.IsNullOrEmpty(signature) || !signature.StartsWith("sha256="))
        {

            _logger.LogWarning("Invalid signature format");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid signature format");
            return;
        }

        context.Request.EnableBuffering();
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        if (!ValidateSignature(body, signature, secret))
        {
            _logger.LogWarning("Invalid signature for webhook request");

            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid signature");
            return;
        }

        await _next(context);
    }

    private static bool ValidateSignature(string payload, string signature, string secret)
    {
        var expectedSignature = "sha256=" + ComputeHmacSha256(payload, secret);

        return string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
