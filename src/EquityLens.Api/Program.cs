using Amazon;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;
using EquityLens.Api.Data;
using EquityLens.Api.Repositories.MarketPrices;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Repositories.Users;
using EquityLens.Api.Services.DemoData;
using EquityLens.Api.Services.DemoUser;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.MarketPrices;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.PortfolioHoldings;
using EquityLens.Api.Services.Portfolios;
using EquityLens.Api.Services.PortfolioValuations;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.Securities;
using EquityLens.Api.Services.UploadedFiles;
using Microsoft.Extensions.Options;

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
builder.Services.AddScoped<IMarketPriceRepository, MarketPriceRepository>();

builder.Services.AddScoped<IDemoUserContext, DemoUserContext>();
builder.Services.AddScoped<IDemoDataService, DemoDataService>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddScoped<ISecurityService, SecurityService>();
builder.Services.AddScoped<IPortfolioHoldingService, PortfolioHoldingService>();
builder.Services.AddScoped<IPortfolioValuationService, PortfolioValuationService>();
builder.Services.AddScoped<IMarketPriceService, MarketPriceService>();
builder.Services.AddScoped<IUploadedFileService, UploadedFileService>();

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

builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<AlphaVantageMarketDataProvider>());
builder.Services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<FinMindMarketDataProvider>());

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
