using System.Text;
using System.Web;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.FinancialData;

public sealed class TwseReportDownloadService : ITwseReportDownloadService
{
    private static readonly string QueryUrl = "https://doc.twse.com.tw/server-java/t57sb01";
    private static readonly string PdfBaseUrl = "https://doc.twse.com.tw";

    private readonly EquityLensDbContext _dbContext;
    private readonly HttpClient _httpClient;

    public TwseReportDownloadService(EquityLensDbContext dbContext, HttpClient httpClient)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
    }

    public async Task<TwseReportDownloadResult> DownloadAnnualReportsAsync(
        IReadOnlyList<int> rocYears,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        var securities = await _dbContext.Securities
            .Where(s => s.Exchange == "TWSE" && s.IsActive)
            .OrderBy(s => s.Ticker)
            .ToListAsync(cancellationToken);

        Console.WriteLine($"找到 {securities.Count} 檔 TWSE 個股");

        var attempted = 0;
        var succeeded = 0;
        var skipped = 0;
        var failed = 0;
        var requestCount = 0;

        var entries = new List<string>();

        foreach (var security in securities)
        {
            foreach (var rocYear in rocYears)
            {
                attempted++;
                var ticker = security.Ticker;
                var tickerDir = Path.Combine(outputDir, ticker);
                Directory.CreateDirectory(tickerDir);

                var outputFile = Path.Combine(tickerDir, $"{ticker}_{rocYear}_annual.pdf");

                if (File.Exists(outputFile))
                {
                    var fi = new FileInfo(outputFile);
                    if (fi.Length > 100_000)
                    {
                        skipped++;
                        entries.Add($"{ticker},{security.Name},{rocYear},skipped,{outputFile},{fi.Length},already_exists,");
                        Console.Write("s");
                        continue;
                    }
                }

                try
                {
                    var gregorianYear = rocYear + 1911;

                    var filenames = new[]
                    {
                        $"{gregorianYear}04_{ticker}_AI1.pdf",
                        $"{gregorianYear}04_{ticker}_AI3.pdf",
                    };

                    string? pdfUrl = null;
                    foreach (var fn in filenames)
                    {
                        pdfUrl = await GetPdfUrlWithRetryAsync(ticker, fn, requestCount, cancellationToken);
                        requestCount++;
                        if (pdfUrl is not null)
                            break;
                    }

                    if (pdfUrl is null)
                    {
                        failed++;
                        entries.Add($"{ticker},{security.Name},{rocYear},not_found,,0,no_annual_report_on_twse,");
                        Console.Write("x");
                    }
                    else
                    {
                        var size = await DownloadPdfAsync(pdfUrl, outputFile, cancellationToken);
                        succeeded++;
                        entries.Add($"{ticker},{security.Name},{rocYear},success,{outputFile},{size},,");
                        Console.Write(".");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    entries.Add($"{ticker},{security.Name},{rocYear},error,,0,{HttpUtility.JavaScriptStringEncode(ex.Message)},");
                    Console.Error.WriteLine($"X [{ticker}/{rocYear}]: {ex.Message}");
                }

                await Task.Delay(3000, cancellationToken);
            }
        }

        Console.WriteLine();

        var manifestPath = Path.Combine(outputDir, "download-manifest.csv");
        await File.WriteAllTextAsync(
            manifestPath,
            "ticker,company_name,roc_year,status,file_path,file_size,error" + Environment.NewLine +
            string.Join(Environment.NewLine, entries),
            Encoding.UTF8,
            cancellationToken);

        Console.WriteLine($"Manifest: {manifestPath}");
        return new TwseReportDownloadResult(attempted, succeeded, skipped, failed, manifestPath);
    }

    private async Task<string?> GetPdfUrlWithRetryAsync(string ticker, string filename, int requestCount, CancellationToken ct)
    {
        const int maxRetries = 3;
        const int cooldownEvery = 8;
        const int cooldownMs = 10_000;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            if (requestCount > 0 && requestCount % cooldownEvery == 0)
            {
                Console.Error.Write($"[cooldown {cooldownMs / 1000}s]");
                await Task.Delay(cooldownMs, ct);
            }

            var result = await GetPdfUrlAsync(ticker, filename, ct);

            if (result is not null)
                return result;

            // TWSE returned empty/502 → may be rate-limiting
            if (attempt < maxRetries - 1)
            {
                var backoff = 3000 * (attempt + 1);
                Console.Error.Write($"[retry {attempt + 1}/{maxRetries} wait {backoff}ms]");
                await Task.Delay(backoff, ct);
            }
        }

        return null;
    }

    private async Task<string?> GetPdfUrlAsync(string ticker, string filename, CancellationToken ct)
    {
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("step", "9"),
            new KeyValuePair<string, string>("colorchg", "1"),
            new KeyValuePair<string, string>("kind", "A"),
            new KeyValuePair<string, string>("co_id", ticker),
            new KeyValuePair<string, string>("filename", filename),
        });

        using var response = await _httpClient.PostAsync(QueryUrl, formData, ct);

        if ((int)response.StatusCode == 502)
            return null;

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var html = Encoding.GetEncoding("big5").GetString(body);

        var marker = "/pdf/";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;

        var start = idx;
        var end = html.IndexOf('\'', start);
        if (end < 0) end = html.IndexOf('"', start);
        if (end < 0) return null;

        return PdfBaseUrl + html[start..end];
    }

    private async Task<long> DownloadPdfAsync(string pdfUrl, string outputPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(pdfUrl, ct);
        response.EnsureSuccessStatusCode();

        await using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs, ct);
        return fs.Length;
    }
}
