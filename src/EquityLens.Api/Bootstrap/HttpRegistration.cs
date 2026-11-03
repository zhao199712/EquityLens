using EquityLens.Api.Common;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Auth;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

namespace EquityLens.Api.Bootstrap;

public static class HttpRegistration
{
    public static void AddEquityLensHttp(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers(options => options.Filters.Add<EquityLens.Api.Controllers.Filters.LegacyWorkflowExceptionFilter>());
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "EquityLens.Api",
                Version = "v1"
            });

            options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Authorization header using the Bearer scheme."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearer", document)] = []
            });
        });

        // JWT Authentication
        var jwtKey = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        // CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(
                          "http://localhost:5173",
                          "http://127.0.0.1:5173",
                          "http://127.0.0.1:5174",
                          "http://192.168.50.11:5173",
                          "http://172.25.14.202:5173",
                          "https://equitylens.pages.dev")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        // 付費 LLM 端點的速率限制
        builder.Services.AddEquityLensRateLimiting(builder.Configuration);
        builder.Services.AddEquityLensForwardedHeaders();

        // CurrentUser context
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
    }
}
