using Amazon.S3;
using EquityLens.Api.Data;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.MarketData;
using EquityLens.Api.Services.ObjectStorage;
using EquityLens.Api.Services.Redis;
using EquityLens.Api.Services.RiskAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using StackExchange.Redis;

namespace EquityLens.Api.Bootstrap;

public static class InfrastructureRegistration
{
    public static void AddEquityLensInfrastructure(this WebApplicationBuilder builder)
    {
        // Add services to the container.
        builder.Services.AddDbContext<EquityLensDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL"), o => o.UseVector()));

        builder.Services.Configure<AlphaVantageOptions>(builder.Configuration.GetSection("MarketData:AlphaVantage"));
        builder.Services.Configure<FinMindOptions>(builder.Configuration.GetSection("MarketData:FinMind"));
        builder.Services.Configure<AgentRunQueueOptions>(builder.Configuration.GetSection(AgentRunQueueOptions.SectionName));

        // Redis 設定與服務註冊
        builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("Redis"));
        builder.Services.Configure<RiskPythonOptions>(builder.Configuration.GetSection(RiskPythonOptions.SectionName));
        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString));
        builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();
        builder.Services.AddScoped<IBackgroundJobQueue, RedisBackgroundJobQueue>();
        builder.Services.AddScoped<IAgentRunQueue, RedisAgentRunQueue>();
        builder.Services.AddScoped<IRiskPythonShadowQueue, RedisRiskPythonShadowQueue>();
        builder.Services.AddScoped<IRiskShadowComparisonService, RiskShadowComparisonService>();

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
    }
}
