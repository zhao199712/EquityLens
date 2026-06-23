namespace EquityLens.Api.Services.Ai;

public class DeepSeekOptions
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
}
