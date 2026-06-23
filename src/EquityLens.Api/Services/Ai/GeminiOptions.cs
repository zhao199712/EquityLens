namespace EquityLens.Api.Services.Ai;

public sealed class GeminiOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.0-flash";
}
