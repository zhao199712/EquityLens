using EquityLens.Api.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace EquityLens.Api.Tests.Services.Auth;

public class GoogleTokenValidatorTests
{
    private static GoogleTokenValidator CreateValidator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Google:ClientId"] = "test-client-id.apps.googleusercontent.com"
            })
            .Build();

        return new GoogleTokenValidator(configuration);
    }

    [Fact]
    public async Task ValidateAsync_MalformedToken_ReturnsNull()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("dummy.invalid.token");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_NonBase64Token_ReturnsNull()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("###not-a-token###");

        Assert.Null(result);
    }

    [Fact]
    public void Constructor_MissingClientId_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => new GoogleTokenValidator(configuration));
    }
}
