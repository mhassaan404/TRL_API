//using Microsoft.AspNetCore.Authentication.Cookies;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.IdentityModel.Tokens;
//using System.Text;
//using TRL_API.BLL;
//using TRL_API.Data;
//using TRL_API.Models;
//using TRL_API.Services;

//var builder = WebApplication.CreateBuilder(args);

//// Add DbContext
//builder.Services.AddDbContext<AppDbContext>(options =>
//    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

//// Add TokenService
//builder.Services.AddScoped<ITokenService, TokenService>();

//// Add services
//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

//// Authentication (JWT)
//builder.Services.AddAuthentication("JwtBearer")
//    .AddJwtBearer("JwtBearer", options =>
//    {
//        options.TokenValidationParameters = new TokenValidationParameters
//        {
//            ValidateIssuer = true,
//            ValidateAudience = true,
//            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
//            ValidAudience = builder.Configuration["JwtSettings:Audience"],
//            ValidateIssuerSigningKey = true,
//            IssuerSigningKey = new SymmetricSecurityKey(
//                Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SecretKey"])
//            ),
//            ValidateLifetime = true,
//            ClockSkew = TimeSpan.Zero
//        };

//        // Allow JWT from cookies
//        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
//        {
//            OnMessageReceived = context =>
//            {
//                var token = context.Request.Cookies["jwt"];
//                if (!string.IsNullOrEmpty(token))
//                {
//                    context.Token = token;
//                }
//                return Task.CompletedTask;
//            }
//        };
//    });

//builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//    .AddCookie(options =>
//    {
//        options.LoginPath = "/Auth/login"; // redirect if not authenticated
//        options.ExpireTimeSpan = TimeSpan.FromMinutes(30); // cookie expiry
//        options.SlidingExpiration = true; // refresh cookie on activity
//    });

//// Authorization
//builder.Services.AddAuthorization();

//// Dependency Injection
//builder.Services.Scan(scan => scan
//    .FromAssemblyOf<DashboardService>()
//    .AddClasses(classes => classes.InNamespaces("TRL_API.BLL", "TRL_API.DAL"))
//    .AsSelfWithInterfaces()
//    .WithScopedLifetime()
//);

//// ✅ CORS setup for cookie-based auth
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("AllowFrontend", policy =>
//    {
//        policy.WithOrigins("http://localhost:3000") // your React app URL
//              .AllowAnyMethod()
//              .AllowAnyHeader()
//              .AllowCredentials(); // required for HttpOnly cookies
//    });
//});

//builder.Services.AddScoped<DbHelper>();

//var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//// Middleware order matters
//app.UseHttpsRedirection();

//app.UseCors("AllowFrontend");

//app.UseAuthentication();
//app.UseAuthorization();

//app.MapControllers();

//app.Run();


using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TRL_API.BLL;
using TRL_API.Data;
using TRL_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add TokenService
builder.Services.AddScoped<ITokenService, TokenService>();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SecretKey"])
            ),
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

// ✅ Authentication setup


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
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000") // your React app URL
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // required for HttpOnly cookies
    });
});

builder.Services.AddScoped<DbHelper>();

var app = builder.Build();

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