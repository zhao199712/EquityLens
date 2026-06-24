namespace EquityLens.Api.Services.Chat;

public sealed class BraveOptions
{
    public const string SectionName = "Brave";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.search.brave.com/res/v1/web/search";
    public int DefaultCount { get; set; } = 10;
}
