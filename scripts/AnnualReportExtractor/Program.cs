using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using UglyToad.PdfPig;

// ═══════════════════════════════════════════════════════════════════════════════
// Annual Report Knowledge Extraction — Full TWSE Run
// ═══════════════════════════════════════════════════════════════════════════════

var pdfBaseDir = "/home/ymsh20220/EquityLens/exports/financial-reports";
var outputDir = "/home/ymsh20220/EquityLens";
var connString = "Host=localhost:5432;Database=equitylens;Username=equitylens;Password=equitylens_dev_password";

// ─── Main ───────────────────────────────────────────────────────────────────

Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine("Annual Report Knowledge Extraction — Full TWSE Run");
Console.WriteLine("=".PadRight(60, '='));

// Query all filings from DB
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();
var listSql = """
    SELECT s.ticker, s.name, ff.fiscal_year, uf.original_file_name
    FROM financial_filing ff
    JOIN security s ON s.id = ff.security_id
    JOIN uploaded_file uf ON uf.id = ff.uploaded_file_id
    WHERE ff.filing_type = 'AnnualReport'
    ORDER BY s.ticker, ff.fiscal_year
    """;

var filings = new List<(string Ticker, string Name, int FiscalYear, string FileName)>();
await using (var cmd = new NpgsqlCommand(listSql, conn))
    await using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync())
            filings.Add((reader.GetString(0), reader.GetString(1),
                         reader.GetInt32(2), reader.GetString(3)));

Console.WriteLine($"Filings found: {filings.Count}");
Console.WriteLine($"Companies: {filings.Select(f => f.Ticker).Distinct().Count()}");
Console.WriteLine($"Years: {string.Join(", ", filings.Select(f => f.FiscalYear).Distinct().Order())}");
Console.WriteLine();

var allChunks = new List<Chunk>();
int totalPages = 0, skippedPages = 0;
int processed = 0, failed = 0;
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

foreach (var (ticker, companyName, fiscalYear, fileName) in filings)
{
    processed++;
    var rocYear = fiscalYear - 1911;
    var pdfPath = Path.Combine(pdfBaseDir, ticker, $"{ticker}_{rocYear}_annual.pdf");

    if (!File.Exists(pdfPath))
    {
        Console.WriteLine($"  [{processed,3}/{filings.Count}] {ticker}/{fiscalYear} ✗ PDF not found");
        failed++;
        continue;
    }

    var fileInfo = new FileInfo(pdfPath);
    if (fileInfo.Length < 1000)
    {
        Console.WriteLine($"  [{processed,3}/{filings.Count}] {ticker}/{fiscalYear} ✗ PDF too small ({fileInfo.Length} bytes)");
        failed++;
        continue;
    }

    Console.Write($"  [{processed,3}/{filings.Count}] {ticker} {companyName} {fiscalYear} ({fileInfo.Length / 1024 / 1024:F1}MB)");

    // Extract pages
    List<PdfPage> pages;
    try { pages = ExtractPdfPages(pdfPath); }
    catch (Exception ex)
    {
        Console.WriteLine($" ✗ {ex.Message}");
        failed++;
        continue;
    }

    pageLoop:
    // Classify + collect
    var state = new SectionState();
    int chunkIndex = 0;
    var tickerChunks = new List<Chunk>();

    foreach (var page in pages)
    {
        totalPages++;
        if (!PassesQualityGate(page)) { skippedPages++; continue; }

        var result = state.ClassifyPage(page.Text);

        if (result == Relevance.Keep)
        {
            tickerChunks.Add(new Chunk(
                Ticker: ticker,
                CompanyName: companyName,
                FiscalYear: fiscalYear,
                ChunkIndex: chunkIndex++,
                PageNumber: page.Number,
                SectionTitle: state.CurrentSection,
                Relevance: "Keep",
                Content: page.Text,
                CharCount: page.CharCount,
                ContentHash: Sha256Hex(page.Text),
                SourcePdf: fileName
            ));
        }
        else if (result == Relevance.Skip)
        {
            skippedPages++;
        }
        // Review pages are dropped entirely (not persisted)
    }

    // ── Per-filing post-processing: filter empty refs, excluded pages, property tables ──
    var excludedKeys = new HashSet<string>
    {
        // Manual exclusions from pilot review
        "2317/21",  // IFRS power purchase agreement standard
        "2317/38",  // hedge accounting / provision policy
        "2330/81",  // transition page with appendix references
    };

    var filtered = tickerChunks
        .Where(c => !IsEmptySegmentReference(c))
        .Where(c => !excludedKeys.Contains($"{c.Ticker}/{c.PageNumber}"))
        .Where(c => !IsPropertyTable(c))
        .ToList();

    // ── Caps per filing ──
    const int maxKeep = 60;
    const int maxRelated = 10;

    var relatedParty = filtered.Where(c => c.SectionTitle == "關係人交易")
        .OrderByDescending(c => c.CharCount).Take(maxRelated).ToList();
    var otherKeep = filtered.Where(c => c.SectionTitle != "關係人交易").ToList();

    var remainingBudget = maxKeep - relatedParty.Count;
    var prioritizedOther = otherKeep
        .OrderByDescending(c => c.SectionTitle switch
        {
            "管理層討論" => 10, "風險事項" => 9, "關鍵查核事項" => 9, "財務風險" => 8,
            "主要財報" => 8, "業務內容" => 7, "股利分配" => 7, "未來展望" => 7,
            "借款及契約" => 6, "轉投資明細" => 5, "重大承諾" => 5, "或有事項" => 5,
            "部門資訊" => 4, "產銷概況" => 4, "研發策略" => 4, "財務分析" => 3, _ => 1,
        })
        .ThenByDescending(c => c.CharCount)
        .Take(Math.Max(0, remainingBudget))
        .ToList();

    var capped = relatedParty.Concat(prioritizedOther).OrderBy(c => c.PageNumber).ToList();
    allChunks.AddRange(capped);

    Console.WriteLine($" → {capped.Count} keep ({filtered.Count - capped.Count} capped)");
}

stopwatch.Stop();
Console.WriteLine($"\n  Total elapsed: {stopwatch.Elapsed.TotalMinutes:F1} min");

// ─── Write output ───────────────────────────────────────────────────────────

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
Directory.CreateDirectory(outputDir);

var keepPath = Path.Combine(outputDir, "annual-report-chunks.keep.jsonl");
var totalKeep = allChunks.Count;
var companyCount = allChunks.Select(c => c.Ticker).Distinct().Count();

await using (var f = File.CreateText(keepPath))
    foreach (var c in allChunks)
        await f.WriteLineAsync(JsonSerializer.Serialize(c, jsonOptions));

var fileSize = new FileInfo(keepPath).Length / 1024.0;

Console.WriteLine($"\n{new string('=', 60)}");
Console.WriteLine("✅ 全量萃取完成");
Console.WriteLine($"  Companies:  {companyCount}");
Console.WriteLine($"  Keep:       {totalKeep} chunks");
Console.WriteLine($"  Skip:       {skippedPages} pages");
Console.WriteLine($"  Output:     {keepPath}");
Console.WriteLine($"  Size:       {fileSize:F0} KB");
Console.WriteLine($"  Time:       {stopwatch.Elapsed.TotalMinutes:F1} min");
Console.WriteLine($"{new string('=', 60)}");

// Section distribution
var secCounts = allChunks
    .GroupBy(c => c.SectionTitle)
    .Select(g => (Section: g.Key, Count: g.Count()))
    .OrderByDescending(x => x.Count);

Console.WriteLine("\n📊 Keep 章節分布:");
foreach (var (sec, cnt) in secCounts)
    Console.WriteLine($"  {sec,-22} {cnt,4}");

// ─── Filter: empty segment reference pages ─────────────────────────────────

static bool IsEmptySegmentReference(Chunk chunk)
{
    if (chunk.SectionTitle != "部門資訊") return false;

    // Check if page STARTS with a "see appendix" pointer (even if data follows later)
    var head = chunk.Content.Length > 120 ? chunk.Content[..120] : chunk.Content;
    string[] pointerPhrases = ["參閱", "請參閱", "詳", "詳見", "詳附註"];
    bool startsWithPointer = pointerPhrases.Any(p => head.Contains(p));

    if (!startsWithPointer) return false;

    // Only skip if the data part is minimal (< 30% of page is actual segment data)
    bool hasSegmentData = chunk.Content.Contains("收入") || chunk.Content.Contains("損益") ||
                          chunk.Content.Contains("資產") || chunk.Content.Contains("部門損");
    return !hasSegmentData;
}

// ─── Filter: property acquisition tables (limited investment value) ───────

static bool IsPropertyTable(Chunk chunk)
{
    // Under related-party, property acquisition tables add limited value
    if (chunk.SectionTitle != "關係人交易") return false;

    // Detect property/land acquisition content: 不動產取得, 土地取得, 廠房買賣
    bool hasProperty = chunk.Content.Contains("不動產") && chunk.Content.Contains("取得");
    bool hasLand = chunk.Content.Contains("土地") && chunk.Content.Contains("取得");
    bool hasBuilding = chunk.Content.Contains("廠房") || chunk.Content.Contains("建物");

    // Only flag if the page is mostly tabular (lots of numbers, low narrative density)
    bool isTabular = chunk.CharCount > 200 && chunk.CharCount < 3000;

    return (hasProperty || (hasLand && hasBuilding)) && isTabular;
}


// ═══════════════════════════════════════════════════════════════════════════════
// Local functions
// ═══════════════════════════════════════════════════════════════════════════════

async Task<FilingInfo?> QueryFilingAsync(string connectionString, string ticker, int fiscalYear)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    var sql = """
        SELECT s.ticker, s.name, uf.original_file_name
        FROM financial_filing ff
        JOIN security s ON s.id = ff.security_id
        JOIN uploaded_file uf ON uf.id = ff.uploaded_file_id
        WHERE s.ticker = @ticker AND ff.fiscal_year = @fy AND ff.filing_type = 'AnnualReport'
        LIMIT 1
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("ticker", ticker);
    cmd.Parameters.AddWithValue("fy", fiscalYear);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
        return new FilingInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    return null;
}

List<PdfPage> ExtractPdfPages(string pdfPath)
{
    var pages = new List<PdfPage>();
    using var pdf = PdfDocument.Open(pdfPath);
    foreach (var page in pdf.GetPages())
    {
        var text = page.Text;
        // Normalize PDF whitespace artifacts
        text = Regex.Replace(text, @"\s+", " ").Trim();
        pages.Add(new PdfPage(page.Number, text, text.Length));
    }
    return pages;
}

static bool PassesQualityGate(PdfPage page)
{
    // Reject near-empty pages
    if (page.CharCount < 80) return false;

    var text = page.Text;

    // Reject if mostly non-CJK/non-ASCII garbage (e.g. scanned image artifacts)
    var alphaNumCount = text.Count(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c));
    if (text.Length > 0 && (double)alphaNumCount / text.Length < 0.4) return false;

    // Reject if page is just a section divider (mostly numbers / dashes / whitespace)
    var meaningful = text.Count(c => char.IsLetter(c) || char.IsDigit(c));
    if (text.Length > 0 && (double)meaningful / text.Length < 0.15) return false;

    return true;
}

static string Sha256Hex(string input)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
}


// ═══════════════════════════════════════════════════════════════════════════════
// Type definitions
// ═══════════════════════════════════════════════════════════════════════════════

enum Relevance { Keep, Skip, Review }

record SectionRule(string Name, string[] KeywordsCn, string[] KeywordsEn, Relevance Relevance, int Priority = 0);

record FilingInfo(string Ticker, string CompanyName, string FileName);

record PdfPage(int Number, string Text, int CharCount);

record Chunk(
    string Ticker,
    string CompanyName,
    int FiscalYear,
    int ChunkIndex,
    int PageNumber,
    string SectionTitle,
    string Relevance,         // "Keep" | "Review"
    string Content,
    int CharCount,
    string ContentHash,
    string SourcePdf
);

// ─── Section Classification Rules ────────────────────────────────────────────

static class SectionRules
{
    public static SectionRule[] All { get; } =
    [
        // ═══════════════════════════════════════════════════════════════
        // KEEP — investment-relevant content
        // ═══════════════════════════════════════════════════════════════

        // ── Corporate overview sections ──
        new("致股東報告書",
            ["致股東報告書", "致股東會報告", "營業報告", "營業報告書"], [],
            Relevance.Keep, Priority: 90),

        new("業務內容",
            ["業務內容", "業務概況", "主要業務", "營業項目", "營業內容",
             "主要產品", "服務項目"],
            ["Business Overview", "Description of Business"],
            Relevance.Keep, Priority: 85),

        new("產業概況",
            ["產業概況", "市場概況", "市場分析", "產業分析", "競爭優勢",
             "競爭利基", "市場佔有", "市場地位"],
            ["Industry", "Market Overview"],
            Relevance.Keep, Priority: 80),

        new("產銷概況",
            ["產銷概況", "生產狀況", "銷售狀況", "銷售量", "產能", "產能利用率"],
            ["Production", "Sales"],
            Relevance.Keep, Priority: 75),

        new("管理層討論",
            ["管理層討論", "管理階層討論", "營運分析", "經營結果", "經營績效",
             "營業收入", "營業毛利", "營業淨利", "營業利益"],
            ["MD&A", "Management Discussion", "Management's Discussion",
             "Results of Operations"],
            Relevance.Keep, Priority: 95),

        new("未來展望",
            ["未來展望", "未來發展", "營運展望", "發展策略", "經營策略",
             "未來計畫", "未來動向", "中長期策略"],
            ["Outlook", "Prospects", "Forward-Looking", "Future Plan", "Strategy"],
            Relevance.Keep, Priority: 85),

        new("風險事項",
            ["風險管理", "風險事項", "風險因素", "風險分析", "風險評估"],
            ["Risk Factors", "Risk Management", "Risks"],
            Relevance.Keep, Priority: 95),

        new("財務分析",
            ["財務分析", "財務結構", "財務比率", "獲利能力", "償債能力"],
            ["Financial Analysis", "Financial Highlights"],
            Relevance.Keep, Priority: 60),

        // ── Main financial statements (the actual tables with numbers) ──
        new("主要財報",
            ["合併資產負債表", "合併綜合損益表", "合併現金流量表",
             "合併權益變動表", "個體資產負債表", "個體綜合損益表",
             "個体现金流量表", "個體權益變動表"],
            ["Balance Sheet", "Income Statement", "Statement of Cash",
             "Comprehensive Income", "Statement of Changes in Equity"],
            Relevance.Keep, Priority: 95),

        // ── Financial notes — approved topics only ──
        new("部門資訊",
            ["部門資訊", "部門別", "營運部門", "部門績效", "部門別財務資訊",
             "報告部門", "應報導部門"],
            ["Segment", "Segment Information", "Operating Segments",
             "Reportable Segments"],
            Relevance.Keep, Priority: 92),

        new("主要客戶",
            ["主要客戶", "客戶集中", "客戶資訊", "銷貨客戶", "客戶名稱",
             "客戶風險集中"],
            ["Major Customers", "Customer Concentration", "Significant Customers"],
            Relevance.Keep, Priority: 91),

        new("關係人交易",
            ["關係人交易", "關係人", "關係企業交易", "關係人往來",
             "關係人款項", "與關係人之"],
            ["Related Party", "Related Party Transactions"],
            Relevance.Keep, Priority: 90),

        new("借款及契約",
            ["短期借款", "長期借款", "借款合約", "銀行借款", "借款條件",
             "擔保品", "財務契約", "違約條款", "借款到期", "聯貸"],
            ["Borrowings", "Loans", "Credit Facilities", "Debt Covenants",
             "Loan Agreements"],
            Relevance.Keep, Priority: 89),

        new("財務風險",
            ["流動性風險", "市場風險", "匯率風險", "利率風險", "信用風險",
             "價格風險", "風險暴險", "外幣風險", "財務風險管理"],
            ["Liquidity Risk", "Market Risk", "Currency Risk", "Interest Rate Risk",
             "Credit Risk", "Financial Risk Management"],
            Relevance.Keep, Priority: 88),

        new("或有事項",
            ["或有事項", "或有負債", "或有資產", "訴訟"],
            ["Contingencies", "Contingent Liabilities", "Litigation"],
            Relevance.Keep, Priority: 87),

        new("重大承諾",
            ["重大承諾", "承諾事項", "資本支出承諾", "租賃承諾",
             "購買承諾", "合約承諾"],
            ["Commitments", "Capital Expenditure Commitments", "Contractual Obligations"],
            Relevance.Keep, Priority: 86),

        new("轉投資明細",
            ["轉投資明細", "被投資公司", "投資明細", "長期股權投資",
             "採用權益法", "權益法投資", "子公司明細"],
            ["Investments in Subsidiaries", "Equity Method Investments",
             "Investment Details"],
            Relevance.Keep, Priority: 85),

        new("股利分配",
            ["股利分配", "現金股利", "盈餘分配", "股利政策", "每股股利",
             "配息", "股利金額"],
            ["Dividend Distribution", "Cash Dividend", "Dividend Per Share"],
            Relevance.Keep, Priority: 84),

        // ── R&D strategy (not tax credits or tables) ──
        new("研發策略",
            ["研發策略", "研發方向", "技術發展", "技術策略", "研發計畫",
             "研發布局", "技術布局", "研發投入"],
            ["R&D Strategy", "Technology Development", "Research Pipeline"],
            Relevance.Keep, Priority: 75),

        // ── Key audit matters ──
        new("關鍵查核事項",
            ["關鍵查核事項"],
            ["Key Audit Matters"],
            Relevance.Keep, Priority: 94),

        // ═══════════════════════════════════════════════════════════════
        // SKIP — boilerplate / non-investment
        // ═══════════════════════════════════════════════════════════════

        new("目錄",
            ["目錄", "目次"],
            ["Contents", "Table of Contents"],
            Relevance.Skip, Priority: 99),

        new("會計政策",
            ["重要會計政策", "會計政策之彙總", "會計政策之說明", "會計政策",
             "新發布／修正", "IASB發布", "準則及解釋", "首次適用",
             "國際財務報導準則第", "採用新準則"],
            ["Summary of Material Accounting", "Summary of Significant Accounting",
             "Basis of Preparation", "Statement of Compliance",
             "Changes in Accounting Policies"],
            Relevance.Skip, Priority: 98),

        new("查核報告(標準)",
            ["會計師查核報告", "會計師報告", "查核報告", "查核意見",
             "會計師之責任"],
            ["Independent Auditors", "Report of Independent", "Auditors' Report",
             "Auditor's Responsibility"],
            Relevance.Skip, Priority: 96),

        new("研發抵減",
            ["所得稅抵減", "研發投資抵減", "研發支出抵減", "投資抵減"],
            ["Tax Credits", "R&D Tax Credit"],
            Relevance.Skip, Priority: 60),

        new("股利會計處理",
            ["股利收入", "股利認列", "股利會計"],
            ["Dividend Income", "Dividend Recognition", "Dividend Accounting"],
            Relevance.Skip, Priority: 60),

        new("公司概況",
            ["公司簡介", "公司概況"], [],
            Relevance.Skip, Priority: 50),

        new("公司沿革",
            ["公司沿革", "發展歷程", "成立以來", "設立"],
            ["Company History", "Milestones"],
            Relevance.Skip, Priority: 30),

        new("資本及股份",
            ["資本及股份", "股本形成", "股權結構", "股本來源"],
            ["Capital", "Share Capital", "Capital Structure"],
            Relevance.Skip, Priority: 35),

        new("董事及經理人",
            ["董事及經理人", "董事會", "董事監察人", "經理人", "董事"],
            ["Directors", "Board of Directors", "Management"],
            Relevance.Skip, Priority: 40),

        new("公司治理",
            ["公司治理", "治理結構", "內部控制", "內部稽核",
             "薪酬委員會", "審計委員會", "功能性委員會"],
            ["Corporate Governance", "Internal Control"],
            Relevance.Skip, Priority: 40),

        new("股東服務",
            ["股東服務", "股東會", "聯絡方式", "連絡資訊", "公司地址",
             "股東常會"],
            ["Shareholder", "Contact"],
            Relevance.Skip, Priority: 20),

        new("名詞解釋",
            ["名詞解釋", "用語定義", "用語說明"],
            ["Glossary", "Definitions"],
            Relevance.Skip, Priority: 20),

        new("附件",
            ["附件", "附錄", "附表"],
            ["Appendix", "Exhibit", "Schedule"],
            Relevance.Skip, Priority: 25),
    ];
}

// ─── Section State Machine ──────────────────────────────────────────────────

class SectionState
{
    public string CurrentSection { get; private set; } = "未分類";

    private static string NormalizeCjkSpacing(string text)
    {
        return Regex.Replace(text, @"([\u4e00-\u9fff])\s+(?=[\u4e00-\u9fff])", "$1");
    }

    /// <summary>Returns the classification result for this page.</summary>
    public Relevance ClassifyPage(string text)
    {
        var normalized = NormalizeCjkSpacing(text);
        var head = normalized.Length > 300 ? normalized[..300] : normalized;
        head = head.Trim();

        // Quick skip for audit report boilerplate (standard opinion)
        // Override: if page is auditor's responsibility but has going-concern → Review
        if (IsStandardAuditBoilerplate(head))
        {
            CurrentSection = "查核報告(標準)";
            if (head.Contains("繼續經營") || head.Contains("重大不確定性") ||
                head.Contains("Going Concern"))
            {
                CurrentSection = "查核報告(異常)";
                return Relevance.Review;  // Flag for human review, don't auto-Keep
            }
            return Relevance.Skip;
        }

        foreach (var rule in SectionRules.All.OrderByDescending(r => r.Priority))
        {
            bool matched = false;
            foreach (var kw in rule.KeywordsCn)
            {
                if (head.Contains(kw)) { matched = true; break; }
            }
            if (!matched)
            {
                foreach (var kw in rule.KeywordsEn)
                {
                    if (head.Contains(kw, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
                }
            }
            if (!matched) continue;

            CurrentSection = rule.Name;

            // If rule says Skip, obey immediately
            if (rule.Relevance == Relevance.Skip)
                return Relevance.Skip;

            // KAM is always investment-relevant per user spec — bypass contamination check
            bool isKAM = rule.Name == "關鍵查核事項";

            // If rule says Keep, run contamination check on broader text
            if (!isKAM && (IsContaminatedByAccountingPolicy(normalized) || IsContaminatedForSection(rule.Name, normalized)))
            {
                CurrentSection += "(污染)";
                return Relevance.Review;
            }

            return Relevance.Keep;
        }

        // No rule matched → REVIEW
        CurrentSection = "未分類";
        return Relevance.Review;
    }

    /// <summary>Standard audit boilerplate: opinion paragraph, auditor responsibility, etc.</summary>
    private static bool IsStandardAuditBoilerplate(string head)
    {
        // Must contain audit-related keyword AND NOT key audit matters
        bool isAudit = head.Contains("會計師查核報告") || head.Contains("會計師報告") ||
                       head.Contains("查核報告") || head.Contains("查核意見") ||
                       head.Contains("會計師之責任") || head.Contains("管理階層之責任") ||
                       head.Contains("Independent Auditors") || head.Contains("Auditors' Report");

        bool isKeyAudit = head.Contains("關鍵查核事項") || head.Contains("Key Audit Matters");

        return isAudit && !isKeyAudit;
    }

    /// <summary>
    /// Broad contamination check: pages that look like financial notes but are actually
    /// accounting policy boilerplate should be demoted from Keep to Review.
    /// </summary>
    private static bool IsContaminatedByAccountingPolicy(string text)
    {
        var body = text.Length > 600 ? text[..600] : text;

        // Accounting policy indicators — pages describing HOW things are measured, not WHAT the measurements are
        string[] policyKeywords =
        [
            "會計政策之彙總", "重要會計政策", "會計政策之說明",
            "會計處理", "會計估計", "會計判斷", "會計假設",
            "衡量基礎", "歷史成本", "公允價值衡量",
            "公允價值層級", "公允價值之等級", "公允價值之分類",
            "減損評估", "減損測試方法", "金融資產減損",
            "避險會計之適用", "避險關係",
            "IASB", "準則之修正", "新發布之準則",
            "Summary of Material Accounting", "Basis of Preparation",
        ];

        int hitCount = 0;
        foreach (var kw in policyKeywords)
        {
            if (body.Contains(kw)) hitCount++;
            if (hitCount >= 2) return true;
        }
        return false;
    }

    /// <summary>Section-specific contamination checks.</summary>
    private static bool IsContaminatedForSection(string sectionName, string text)
    {
        var body = text.Length > 600 ? text[..600] : text;

        return sectionName switch
        {
            "主要財報" => IsContaminatedMainFinancials(body),
            "財務風險" => IsContaminatedFinancialRisk(body),
            "股利分配" => IsContaminatedDividend(body),
            "產業概況" => IsContaminatedIndustry(body),
            _ => false,
        };
    }

    // 主要財報: must contain actual statement line items, not accounting policies
    private static bool IsContaminatedMainFinancials(string body)
    {
        // Red flags: these indicate accounting policy notes, not the actual statement table
        string[] redFlags =
        [
            "租賃負債", "租賃協議", "租賃期間", "租賃給付",
            "使用權資產", "無形資產之", "無形資產攤",
            "公允價值層級", "公允價值衡量之", "公允價值等級",
            "會計政策之", "會計處理之", "會計估計",
        ];

        int hits = 0;
        foreach (var kw in redFlags) if (body.Contains(kw)) hits++;
        return hits >= 2;
    }

    // 財務風險: real risk disclosures have amounts/numbers, not accounting policy
    private static bool IsContaminatedFinancialRisk(string body)
    {
        string[] redFlags =
        [
            "金融資產減損", "減損評估", "減損方法",
            "避險會計之", "避險關係之", "避險工具之",
            "公允價值層級", "公允價值分類",
            "信用風險管理" + "會計", // "credit risk management" followed by accounting talk
        ];

        // Only flag if the page is clearly about accounting policy, not risk amounts
        int policyHits = 0;
        foreach (var kw in redFlags) if (body.Contains(kw)) policyHits++;

        // Check for actual risk amounts (mitigating factor)
        bool hasRiskAmounts = body.Contains("暴險") || body.Contains("風險集中");

        return policyHits >= 2 && !hasRiskAmounts;
    }

    // 股利分配: must be about actual dividend distribution, not accounting treatment
    private static bool IsContaminatedDividend(string body)
    {
        string[] redFlags =
        [
            "股利收入", "股利認列", "股利會計",
            "限制員工權利新股", "員工酬勞", "員工分紅",
            "Dividend Income", "Dividend Recognition",
        ];
        foreach (var kw in redFlags) if (body.Contains(kw)) return true;
        return false;
    }

    // 產業概況: must have genuine industry context keywords
    private static bool IsContaminatedIndustry(string body)
    {
        // Page matched "產業概況" at head, but verify it's actually about industry
        bool hasIndustryContext = body.Contains("需求") || body.Contains("競爭") ||
            body.Contains("市場") || body.Contains("趨勢") || body.Contains("產品");
        return !hasIndustryContext;
    }
}
