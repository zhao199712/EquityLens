using System.Security.Cryptography;
using System.Text;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Documents;

public sealed class ConferenceChunkingService : IConferenceChunkingService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly IObjectStorageService _objectStorage;
    private readonly IPdfTextExtractionService _pdfTextExtraction;

    public ConferenceChunkingService(
        EquityLensDbContext dbContext,
        IObjectStorageService objectStorage,
        IPdfTextExtractionService pdfTextExtraction)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
        _pdfTextExtraction = pdfTextExtraction;
    }

    public async Task<ConferenceChunkingResult> ChunkConferencesAsync(CancellationToken cancellationToken = default)
    {
        var conferences = await _dbContext.InvestorConferences
            .Include(x => x.Security)
            .Include(x => x.UploadedFile)
            .Include(x => x.Document)
                .ThenInclude(d => d!.Chunks)
            .Where(x => x.UploadedFileId != null && x.DocumentId != null)
            .OrderBy(x => x.Security.Ticker)
            .ThenBy(x => x.OriginalFileName)
            .ToListAsync(cancellationToken);

        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var skippedPages = 0;
        var chunksCreated = 0;

        foreach (var conference in conferences)
        {
            var document = conference.Document!;
            var uploadedFile = conference.UploadedFile!;
            var ticker = conference.Security.Ticker;

            if (document.Chunks.Count > 0)
            {
                Console.WriteLine($"  → {ticker}/{uploadedFile.OriginalFileName}: 已有 chunks，跳過");
                skipped++;
                continue;
            }

            try
            {
                document.ParseStatus = "Parsing";
                conference.ParseStatus = "Parsing";
                await _dbContext.SaveChangesAsync(cancellationToken);

                await using var pdfStream = await _objectStorage.DownloadAsync(uploadedFile.ObjectKey, cancellationToken);
                var pages = await _pdfTextExtraction.ExtractPagesAsync(pdfStream, cancellationToken);

                var chunkIndex = 0;
                foreach (var page in pages.Where(p => !string.IsNullOrWhiteSpace(p.Text)))
                {
                    var content = BuildChunkContent(conference, page);
                    if (IsNoiseChunk(content))
                    {
                        skippedPages++;
                        continue;
                    }

                    _dbContext.DocumentChunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        ChunkIndex = chunkIndex++,
                        Content = content,
                        TokenCount = EstimateTokenCount(content),
                        PageNumber = page.PageNumber,
                        SectionTitle = $"Page {page.PageNumber}",
                        ContentHash = ComputeSha256(content),
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                document.ParseStatus = chunkIndex > 0 ? "Completed" : "Failed";
                document.ParsedAtUtc = DateTime.UtcNow;
                conference.ParseStatus = document.ParseStatus;

                await _dbContext.SaveChangesAsync(cancellationToken);
                chunksCreated += chunkIndex;
                succeeded++;
                Console.WriteLine($"  ✓ {ticker}/{uploadedFile.OriginalFileName}: {chunkIndex} chunks");
            }
            catch (Exception ex)
            {
                document.ParseStatus = "Failed";
                document.ParsedAtUtc = DateTime.UtcNow;
                conference.ParseStatus = "Failed";
                await _dbContext.SaveChangesAsync(cancellationToken);

                failed++;
                Console.WriteLine($"  ✗ {ticker}/{uploadedFile.OriginalFileName}: chunk 失敗 - {ex.Message}");
            }
        }

        return new ConferenceChunkingResult(conferences.Count, succeeded, failed, skipped, chunksCreated, skippedPages);
    }

    private static string BuildChunkContent(InvestorConference conference, PdfPageText page)
    {
        var security = conference.Security;
        var eventDate = conference.EventDate?.ToString("yyyy-MM-dd") ?? "unknown";

        return $"""
Company: {security.Name}
Ticker: {security.Ticker}
Exchange: {security.Exchange}
DocumentType: EarningsPresentation
Source: InvestorConference
ConferenceDate: {eventDate}
Language: {conference.Language}
Page: {page.PageNumber}

{page.Text}
""";
    }

    private static int EstimateTokenCount(string content)
    {
        return Math.Max(1, content.Length / 4);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsNoiseChunk(string content)
    {
        var normalized = content.Replace("\n", " ").Replace("\t", " ").Replace(" ", string.Empty);
        return ContainsAny(normalized, ["SafeHarborNotice", "forward-lookingstatements", "Agenda", "會議議程"]);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
