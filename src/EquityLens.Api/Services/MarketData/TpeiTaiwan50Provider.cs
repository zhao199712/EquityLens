using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace EquityLens.Api.Services.MarketData;

/// <summary>
/// TPEX 0050 成分股提供者，從證櫃 API 取得 CSV 格式成分股清單。
/// </summary>
public sealed class TpeiTaiwan50Provider : ITaiwan50ConstituentProvider
{
    private const string ConstituentsUrl =
        "https://www.tpex.org.tw/web/stock/iNdex_info/gretai50/ingrid/r50cnstnt_result.php?l=zh-tw&o=data";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 TPEX 0050 成分股提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客戶端。</param>
    public TpeiTaiwan50Provider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Taiwan50Constituent>> GetConstituentsAsync(
        CancellationToken cancellationToken = default)
    {
        var csvBytes = await _httpClient.GetByteArrayAsync(ConstituentsUrl, cancellationToken);
        var csv = Encoding.UTF8.GetString(csvBytes);

        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        });

        var results = new List<Taiwan50Constituent>();
        await csvReader.ReadAsync();
        csvReader.ReadHeader();

        while (await csvReader.ReadAsync())
        {
            var stockCode = csvReader.GetField("股票代號")?.Trim().Trim('"') ?? "";
            var name = csvReader.GetField("股票名稱")?.Trim().Trim('"') ?? "";
            if (!string.IsNullOrEmpty(stockCode))
                results.Add(new Taiwan50Constituent(stockCode, name));
        }

        return results;
    }
}
