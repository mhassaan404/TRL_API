using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using TRL_API.BLL;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;
using TRL_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Multi-client: one database per client. The catalog (ConnectionStrings:Catalog) lists the clients; each client's
// connection string is in ClientConnections:<ConnectionKey>. The database for a request comes only from the signed
// access token's client id (see OnTokenValidated below and Data/ClientContext.cs). AppDbContext (Users/RefreshTokens)
// is created per client by AuthController, not registered here.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IClientCatalog, ClientCatalog>();
builder.Services.AddScoped<HttpClientContext>();
builder.Services.AddScoped<IClientContext>(sp => sp.GetRequiredService<HttpClientContext>());

// Add TokenService
builder.Services.AddScoped<ITokenService, TokenService>();

// Add services
//builder.Services.AddControllers();
builder.Services.AddControllers(options => options.Filters.Add<ApiResponseResultFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// Invalid request bodies/query values get the same ApiResponse shape as every other error
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var keys = context.ModelState.Where(e => e.Value?.Errors.Count > 0).Select(e => e.Key).ToList();
        // Prefer the JSON field paths ($.month); the bare parameter name only says "the body was invalid"
        if (keys.Any(k => k.StartsWith("$")))
            keys = keys.Where(k => k.StartsWith("$")).ToList();
        var fields = keys.Select(k => string.IsNullOrEmpty(k) || k == "$" ? "request body" : k.TrimStart('$', '.')).Distinct();
        var message = "Invalid value for: " + string.Join(", ", fields) + ".";
        return new BadRequestObjectResult(new ApiResponse { IsSuccess = false, Message = message, ErrorMessage = message });
    };
});
builder.Services.AddEndpointsApiExplorer();
// Swagger "Authorize" button: paste an access token to call protected endpoints (header Authorization: Bearer ...)
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});

// Secret comes from user-secrets (dev) or the JwtSettings__SecretKey environment variable (prod), never appsettings.json
var jwtSecret = builder.Configuration["JwtSettings:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException("JwtSettings:SecretKey is missing or shorter than 32 bytes. Set it via user-secrets or the JwtSettings__SecretKey environment variable.");

builder.Services.AddAuthentication("JwtBearer")
    .AddJwtBearer("JwtBearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Allow JWT from cookies
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Cookies["jwt"];
                if (!string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },

            // Every authenticated request re-checks the token's client: it must exist, be active, have a database
            // configured on this server and be on the schema this API needs. The client found here (never anything
            // from the request itself) decides which database the request uses.
            OnTokenValidated = async context =>
            {
                var cid = context.Principal?.FindFirst(HttpClientContext.ClaimType)?.Value;
                var catalog = context.HttpContext.RequestServices.GetRequiredService<IClientCatalog>();
                var client = int.TryParse(cid, out var clientId) ? await catalog.GetByIdAsync(clientId) : null;
                if (client == null || !client.IsActive || client.ConnectionString == null || !await catalog.IsSchemaCurrentAsync(client))
                {
                    context.Fail("The client for this session is not available.");
                    return;
                }
                context.HttpContext.Items[HttpClientContext.ItemKey] = client;
            }
        };
    });


// Authorization
builder.Services.AddAuthorization();

// Dependency Injection
builder.Services.Scan(scan => scan
    .FromAssemblyOf<DashboardService>()
    .AddClasses(classes => classes.InNamespaces("TRL_API.BLL", "TRL_API.DAL"))
    .AsSelfWithInterfaces()
    .WithScopedLifetime()
);

// ✅ CORS setup for cookie-based auth
// Frontend origins allowed to call the API with cookies: Cors:AllowedOrigins in appsettings, or the
// Cors__AllowedOrigins__0 (, __1, ...) environment variables in production.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // required for HttpOnly cookies
    });
});

builder.Services.AddScoped<DbHelper>();

// Login protection: per-username lockout after repeated failures (LoginThrottle) plus a per-IP limit on /Auth/login.
// Behind a reverse proxy, configure forwarded headers so RemoteIpAddress is the client, not the proxy.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        const string message = "Too many login attempts from this address. Wait a minute and try again.";
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new ApiResponse { IsSuccess = false, Message = message, ErrorMessage = message }, token);
    };
});

var app = builder.Build();

// Unhandled errors: log the details on the server, send the client a generic ApiResponse (never exception text).
// CORS is applied again inside the handler because the error response is cleared, which drops the CORS headers.
app.UseExceptionHandler(errorApp =>
{
    errorApp.UseCors("AllowFrontend");
    errorApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        // A request the server refused to read (e.g. body over the size limit) is the caller's problem, not a 500
        if (error is Microsoft.AspNetCore.Http.BadHttpRequestException badRequest)
        {
            app.Logger.LogWarning("Bad request on {Method} {Path}: {Message}", context.Request.Method, context.Request.Path, badRequest.Message);
            var tooLarge = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge;
            var text = tooLarge ? "The request is too large." : "The request could not be read.";
            context.Response.StatusCode = tooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ApiResponse { IsSuccess = false, Message = text, ErrorMessage = text });
            return;
        }

        app.Logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);

        const string message = "Something went wrong while processing your request. Please try again.";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiResponse { IsSuccess = false, Message = message, ErrorMessage = message });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    // Swagger calls need the same CSRF header as the frontend
    app.UseSwaggerUI(options =>
        options.UseRequestInterceptor("(req) => { req.headers['X-Requested-With'] = 'XMLHttpRequest'; return req; }"));
}

// Middleware order is important
app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

// CSRF protection: auth rides in cookies, so a state-changing request must carry X-Requested-With: XMLHttpRequest.
// A cross-site form or link can't set that header, and a cross-site script can't either unless CORS allows its origin.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var changesState = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
    if (changesState && context.Request.Headers["X-Requested-With"] != "XMLHttpRequest")
    {
        const string message = "Request blocked: missing X-Requested-With header.";
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ApiResponse { IsSuccess = false, Message = message, ErrorMessage = message });
        return;
    }
    await next();
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();