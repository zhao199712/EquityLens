using System.Text;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.ExchangeRates;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.Auth;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.DemoData;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.ExchangeRates;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.PortfolioHoldings;
using EquityLens.Api.Services.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.Securities;
using EquityLens.Api.Services.UploadedFiles;
using EquityLens.Api.Services.FinancialFilings;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<EquityLensDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL"), o => o.UseVector()));

builder.Services.Configure<AlphaVantageOptions>(builder.Configuration.GetSection("MarketData:AlphaVantage"));
builder.Services.Configure<FinMindOptions>(builder.Configuration.GetSection("MarketData:FinMind"));

// Redis 設定與服務註冊
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString));
builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
builder.Services.AddScoped<IBackgroundJobQueue, RedisBackgroundJobQueue>();

// S3 相容物件儲存設定與服務註冊
builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection("ObjectStorage"));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
    var config = new AmazonS3Config
    {
        ServiceURL = options.ServiceUrl,
        ForcePathStyle = options.ForcePathStyle,
        AuthenticationRegion = options.Region
    };
    return new AmazonS3Client(options.AccessKeyId, options.SecretAccessKey, config);
});
builder.Services.AddScoped<IObjectStorageService, S3ObjectStorageService>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<ISecurityRepository, SecurityRepository>();
builder.Services.AddScoped<IPortfolioHoldingRepository, PortfolioHoldingRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IMarketPriceRepository, MarketPriceRepository>();
builder.Services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();

builder.Services.AddScoped<IDemoUserContext, DemoUserContext>();
builder.Services.AddScoped<IDemoDataService, DemoDataService>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddScoped<ISecurityService, SecurityService>();
builder.Services.AddScoped<IPortfolioHoldingService, PortfolioHoldingService>();
builder.Services.AddScoped<IPortfolioValuationService, PortfolioValuationService>();
builder.Services.AddScoped<IMarketPriceService, MarketPriceService>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<IUploadedFileService, UploadedFileService>();
builder.Services.AddScoped<IFinancialFilingService, FinancialFilingService>();

builder.Services.AddHttpClient<AlphaVantageMarketDataProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AlphaVantageOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddHttpClient<FinMindMarketDataProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<FinMindOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddHttpClient<YahooFinanceMarketDataProvider>(client =>
{
    client.BaseAddress = new Uri("https://query1.finance.yahoo.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
});

builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<YahooFinanceMarketDataProvider>());
builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<AlphaVantageMarketDataProvider>());
builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<FinMindMarketDataProvider>());

builder.Services.AddControllers();
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
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// CurrentUser context
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
