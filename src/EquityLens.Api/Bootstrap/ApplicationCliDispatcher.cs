using EquityLens.Api.Bootstrap;
using EquityLens.Api.Data;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.FinancialData;
using EquityLens.Api.Services.InvestorConferences;

namespace EquityLens.Api.Bootstrap;

public static class ApplicationCliDispatcher
{
    public static async Task<bool> TryExecuteAsync(WebApplication app, string[] args)
    {
        // 匯入法說會模式: dotnet run -- --import-conferences
        if (args.Contains("--import-conferences"))
        {
            using var scope = app.Services.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IConferenceImportService>();
            Console.WriteLine("開始匯入法說會 PDF 到 Garage...");
            var result = await importService.ImportAllAsync();
            Console.WriteLine($"===== 匯入完成 =====");
            Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped}");
            return true;
        }

        // 法說會 PDF 切 chunk 模式: dotnet run -- --chunk-conferences
        if (args.Contains("--chunk-conferences"))
        {
            using var scope = app.Services.CreateScope();
            var chunkingService = scope.ServiceProvider.GetRequiredService<IConferenceChunkingService>();
            Console.WriteLine("開始解析法說會 PDF 並寫入 document_chunk...");
            var result = await chunkingService.ChunkConferencesAsync();
            Console.WriteLine("===== chunk 完成 =====");
            Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped} | chunks: {result.ChunksCreated}");
            return true;
        }

        // 產生缺少的 embedding: dotnet run -- --embed-chunks
        if (args.Contains("--embed-chunks"))
        {
            using var scope = app.Services.CreateScope();
            var embeddingService = scope.ServiceProvider.GetRequiredService<IChunkEmbeddingService>();
            Console.WriteLine("開始產生 document_embedding...");
            var result = await embeddingService.EmbedMissingChunksAsync();
            Console.WriteLine("===== embedding 完成 =====");
            Console.WriteLine($"待處理: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 未處理: {result.Skipped}");
            return true;
        }

        // 匯出 embedding JSONL: dotnet run -- --export-embeddings ./exports/conference-embeddings.jsonl
        if (args.Contains("--import-finmind-financials") && !args.Contains("--help"))
        {
            var fromIndex = Array.IndexOf(args, "--from");
            var toIndex = Array.IndexOf(args, "--to");
            var from = fromIndex >= 0 && fromIndex + 1 < args.Length ? DateOnly.Parse(args[fromIndex + 1]) : new DateOnly(2023, 1, 1);
            var to = toIndex >= 0 && toIndex + 1 < args.Length ? DateOnly.Parse(args[toIndex + 1]) : DateOnly.FromDateTime(DateTime.Today);

            using var scope = app.Services.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IFinMindFinancialImportService>();
            Console.WriteLine($"開始從 FinMind 匯入財報結構化數字 ({from:yyyy-MM-dd} ~ {to:yyyy-MM-dd})...");
            var result = await importService.ImportAsync(from, to);
            Console.WriteLine("===== 匯入完成 =====");
            Console.WriteLine($"Statements: {result.TotalStatements} | LineItems: {result.TotalLineItems} | 成功: {result.Succeeded} | 失敗: {result.Failed}");
            return true;
        }

        // MOPS 匯入財報模式: dotnet run -- --import-mops-financials
        if (!args.Contains("--help") && args.Contains("--import-mops-financials"))
        {
            var fromIndex = Array.IndexOf(args, "--from");
            var toIndex = Array.IndexOf(args, "--to");
            var from = fromIndex >= 0 && fromIndex + 1 < args.Length ? DateOnly.Parse(args[fromIndex + 1]) : new DateOnly(2023, 1, 1);
            var to = toIndex >= 0 && toIndex + 1 < args.Length ? DateOnly.Parse(args[toIndex + 1]) : DateOnly.FromDateTime(DateTime.Today);

            using var scope = app.Services.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IMopsFinancialImportService>();
            Console.WriteLine($"開始從 MOPS 匯入財報結構化數字 ({from:yyyy-MM-dd} ~ {to:yyyy-MM-dd})...");
            var result = await importService.ImportAsync(from, to);
            Console.WriteLine("===== 匯入完成 =====");
            Console.WriteLine($"Statements: {result.StatementsCreated} | LineItems: {result.LineItemsCreated} | 成功: {result.Succeeded} | 失敗: {result.Failed}");
            return true;
        }

        // 下載 TWSE 財報年報 PDF: dotnet run -- --download-twse-reports
        if (args.Contains("--download-twse-reports"))
        {
            using var scope = app.Services.CreateScope();
            var downloadService = scope.ServiceProvider.GetRequiredService<ITwseReportDownloadService>();

            // Resolve default output relative to repo root (walk up from src/EquityLens.Api)
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var defaultOutput = Path.Combine(repoRoot, "exports", "financial-reports");
            var outputDir = args.Contains("--output")
                ? Path.GetFullPath(args[Array.IndexOf(args, "--output") + 1])
                : defaultOutput;

            var result = await downloadService.DownloadAnnualReportsAsync([112, 113, 114], outputDir);
            Console.WriteLine($"===== 下載完成 =====");
            Console.WriteLine($"嘗試: {result.Attempted} | 成功: {result.Succeeded} | 略過: {result.Skipped} | 失敗: {result.Failed}");
            Console.WriteLine($"Manifest: {result.ManifestPath}");
            return true;
        }

        // 上傳已下載的 TWSE 年報 PDF 到 Garage: dotnet run -- --import-twse-report-files
        if (args.Contains("--import-twse-report-files"))
        {
            using var scope = app.Services.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<ITwseReportFileImportService>();
            Console.WriteLine("開始上傳 TWSE 年報 PDF 到 Garage 並寫入 metadata...");
            var result = await importService.ImportAllAsync();
            Console.WriteLine("===== 匯入完成 =====");
            Console.WriteLine($"總數: {result.Total} | 成功: {result.Succeeded} | 失敗: {result.Failed} | 跳過: {result.Skipped}");
            return true;
        }

        // 匯出 embedding JSONL: dotnet run -- --export-embeddings ./exports/conference-embeddings.jsonl
        if (args.Contains("--export-embeddings"))
        {
            var index = Array.IndexOf(args, "--export-embeddings");
            var outputPath = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : "exports/conference-embeddings.jsonl";

            using var scope = app.Services.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IEmbeddingExportService>();
            Console.WriteLine($"開始匯出 document_embedding 到 {outputPath}...");
            var result = await exportService.ExportAsync(outputPath);
            Console.WriteLine("===== 匯出完成 =====");
            Console.WriteLine($"筆數: {result.Exported} | 路徑: {result.OutputPath}");
            return true;
        }

        // 匯出財務結構化數字 CSV: dotnet run -- --export-financials-csv ./exports/financials.csv
        if (args.Contains("--export-financials-csv"))
        {
            var idx = Array.IndexOf(args, "--export-financials-csv");
            var csvPath = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : "exports/financial-statements.csv";

            using var scope = app.Services.CreateScope();
            await new FinancialCsvExportService(scope.ServiceProvider.GetRequiredService<EquityLensDbContext>()).ExportAsync(csvPath);
            return true;
        }
        return false;
    }
}
