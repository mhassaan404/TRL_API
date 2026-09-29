using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using TRL_API.BLL;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;
using TRL_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddSwaggerGen();

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

var app = builder.Build();

// Unhandled errors: log the details on the server, send the client a generic ApiResponse (never exception text).
// CORS is applied again inside the handler because the error response is cleared, which drops the CORS headers.
app.UseExceptionHandler(errorApp =>
{
    errorApp.UseCors("AllowFrontend");
    errorApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);

        const string message = "Something went wrong while processing your request. Please try again.";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiResponse { IsSuccess = false, Message = message, ErrorMessage = message });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware order is important
app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();