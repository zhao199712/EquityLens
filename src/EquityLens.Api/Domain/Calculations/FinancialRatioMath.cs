namespace EquityLens.Api.Domain.Calculations;

/// <summary>
/// 財報分析與估值數學函數，提供獲利能力、成長性、償債能力、流動性與估值倍數等財務比率。
/// 所有計算均為純函數，基於基本面分析模型（Fundamental Ratio Analysis），無外部依賴。
/// 分母為零或無意義時回傳 null，呼叫端應自行判斷 null 的業務語意。
/// </summary>
public static class FinancialRatioMath
{
    /// <summary>
    /// 計算毛利率（Gross Margin）。
    /// </summary>
    /// <param name="grossProfit">營業毛利。</param>
    /// <param name="revenue">營業收入。</param>
    /// <returns>毛利率 = grossProfit / revenue。若營收為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：基本面獲利能力比率模型（Profitability Ratio - Gross Margin）。
    /// 公式：Gross Margin = Gross Profit / Revenue。
    /// 衡量公司本業產品的獲利能力，排除營業費用、利息和稅的影響。
    /// </remarks>
    public static decimal? CalculateGrossMargin(decimal grossProfit, decimal revenue)
    {
        return revenue == 0 ? null : grossProfit / revenue;
    }

    /// <summary>
    /// 計算營業利益率（Operating Margin）。
    /// </summary>
    /// <param name="operatingIncome">營業利益。</param>
    /// <param name="revenue">營業收入。</param>
    /// <returns>營業利益率 = operatingIncome / revenue。若營收為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：基本面獲利能力比率模型（Profitability Ratio - Operating Margin）。
    /// 公式：Operating Margin = Operating Income / Revenue。
    /// 衡量公司本業經營效率，包含營業費用但不含利息和稅。
    /// </remarks>
    public static decimal? CalculateOperatingMargin(decimal operatingIncome, decimal revenue)
    {
        return revenue == 0 ? null : operatingIncome / revenue;
    }

    /// <summary>
    /// 計算淨利率（Net Margin）。
    /// </summary>
    /// <param name="netIncome">稅後淨利。</param>
    /// <param name="revenue">營業收入。</param>
    /// <returns>淨利率 = netIncome / revenue。若營收為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：基本面獲利能力比率模型（Profitability Ratio - Net Margin）。
    /// 公式：Net Margin = Net Income / Revenue。
    /// 衡量公司最終的賺錢效率，已扣除所有成本、費用、利息和稅。
    /// </remarks>
    public static decimal? CalculateNetMargin(decimal netIncome, decimal revenue)
    {
        return revenue == 0 ? null : netIncome / revenue;
    }

    /// <summary>
    /// 計算股東權益報酬率（ROE, Return on Equity）。
    /// </summary>
    /// <param name="netIncome">稅後淨利。</param>
    /// <param name="shareholdersEquity">股東權益總額。</param>
    /// <returns>ROE = netIncome / shareholdersEquity。若股東權益為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：杜邦分析核心比率模型（DuPont Analysis - ROE）。
    /// 公式：ROE = Net Income / Shareholders' Equity。
    /// 衡量公司利用股東資金創造利潤的效率。
    /// ROE &gt; 15% 通常被視為良好，但需搭配負債比率一起判斷。
    /// 杜邦分解：ROE = Net Margin × Asset Turnover × Equity Multiplier。
    /// </remarks>
    public static decimal? CalculateRoe(decimal netIncome, decimal shareholdersEquity)
    {
        return shareholdersEquity == 0 ? null : netIncome / shareholdersEquity;
    }

    /// <summary>
    /// 計算資產報酬率（ROA, Return on Assets）。
    /// </summary>
    /// <param name="netIncome">稅後淨利。</param>
    /// <param name="totalAssets">總資產。</param>
    /// <returns>ROA = netIncome / totalAssets。若總資產為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：基本面獲利效率比率模型（Profitability Ratio - ROA）。
    /// 公式：ROA = Net Income / Total Assets。
    /// 衡量公司利用全部資產創造利潤的效率，反映管理層運用資源的能力。
    /// </remarks>
    public static decimal? CalculateRoa(decimal netIncome, decimal totalAssets)
    {
        return totalAssets == 0 ? null : netIncome / totalAssets;
    }

    /// <summary>
    /// 計算負債權益比（Debt-to-Equity Ratio）。
    /// </summary>
    /// <param name="totalLiabilities">總負債。</param>
    /// <param name="shareholdersEquity">股東權益總額。</param>
    /// <returns>負債權益比 = totalLiabilities / shareholdersEquity。若股東權益為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：財務槓桿比率模型（Leverage Ratio - D/E Ratio）。
    /// 公式：D/E = Total Liabilities / Shareholders' Equity。
    /// 衡量公司財務槓桿程度。
    /// D/E &lt; 1 為穩健；D/E &gt; 2 為高槓桿，風險較高但仍需視產業特性判斷。
    /// </remarks>
    public static decimal? CalculateDebtToEquity(decimal totalLiabilities, decimal shareholdersEquity)
    {
        return shareholdersEquity == 0 ? null : totalLiabilities / shareholdersEquity;
    }

    /// <summary>
    /// 計算流動比率（Current Ratio）。
    /// </summary>
    /// <param name="currentAssets">流動資產。</param>
    /// <param name="currentLiabilities">流動負債。</param>
    /// <returns>流動比率 = currentAssets / currentLiabilities。若流動負債為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：短期償債能力比率模型（Liquidity Ratio - Current Ratio）。
    /// 公式：Current Ratio = Current Assets / Current Liabilities。
    /// 衡量公司一年內償還短期債務的能力。
    /// Current Ratio &gt; 2 通常被視為安全，但過高可能表示資金效率不佳。
    /// </remarks>
    public static decimal? CalculateCurrentRatio(decimal currentAssets, decimal currentLiabilities)
    {
        return currentLiabilities == 0 ? null : currentAssets / currentLiabilities;
    }

    /// <summary>
    /// 計算速動比率（Quick Ratio / Acid-Test Ratio）。
    /// </summary>
    /// <param name="currentAssets">流動資產。</param>
    /// <param name="inventory">存貨。</param>
    /// <param name="currentLiabilities">流動負債。</param>
    /// <returns>速動比率 = (currentAssets - inventory) / currentLiabilities。若流動負債為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：嚴格短期償債能力比率模型（Liquidity Ratio - Quick Ratio）。
    /// 公式：Quick Ratio = (Current Assets - Inventory) / Current Liabilities。
    /// 比流動比率更嚴格，排除變現速度較慢的存貨。
    /// Quick Ratio &gt; 1 為安全，表示無需出售存貨也能償還短期負債。
    /// </remarks>
    public static decimal? CalculateQuickRatio(decimal currentAssets, decimal inventory, decimal currentLiabilities)
    {
        return currentLiabilities == 0 ? null : (currentAssets - inventory) / currentLiabilities;
    }

    /// <summary>
    /// 計算營收成長率（Revenue Growth Rate）。
    /// </summary>
    /// <param name="currentRevenue">本期營業收入。</param>
    /// <param name="previousRevenue">前期營業收入。</param>
    /// <returns>營收成長率 = (current - previous) / previous。若前期營收為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：同比成長率模型（YoY Growth Rate）。
    /// 公式：Growth = (Revenue_current - Revenue_previous) / Revenue_previous。
    /// 正值表示成長；負值表示衰退。
    /// </remarks>
    public static decimal? CalculateRevenueGrowth(decimal currentRevenue, decimal previousRevenue)
    {
        return previousRevenue == 0 ? null : (currentRevenue - previousRevenue) / previousRevenue;
    }

    /// <summary>
    /// 計算每股盈餘成長率（EPS Growth Rate）。
    /// </summary>
    /// <param name="currentEps">本期每股盈餘。</param>
    /// <param name="previousEps">前期每股盈餘。</param>
    /// <returns>EPS 成長率 = (current - previous) / previous。若前期 EPS 為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：同比成長率模型（YoY EPS Growth Rate）。
    /// 公式：EPS Growth = (EPS_current - EPS_previous) / EPS_previous。
    /// 衡量公司每股獲利能力的成長趨勢。
    /// </remarks>
    public static decimal? CalculateEpsGrowth(decimal currentEps, decimal previousEps)
    {
        return previousEps == 0 ? null : (currentEps - previousEps) / previousEps;
    }

    /// <summary>
    /// 計算本益比（P/E Ratio, Price-to-Earnings）。
    /// </summary>
    /// <param name="price">股價。</param>
    /// <param name="eps">每股盈餘。</param>
    /// <returns>本益比 = price / eps。若 EPS 小於等於零則回傳 null（虧損公司 P/E 無意義）。</returns>
    /// <remarks>
    /// 模型：估值倍數模型（Valuation Multiple - P/E Ratio）。
    /// 公式：P/E = Price / EPS。
    /// 衡量市場願意為每單位盈餘付出多少價格。
    /// P/E 愈低可能表示股價被低估（也可能是前景不佳）；P/E 愈高可能表示成長期待高（也可能是泡沫）。
    /// </remarks>
    public static decimal? CalculatePeRatio(decimal price, decimal eps)
    {
        return eps <= 0 ? null : price / eps;
    }

    /// <summary>
    /// 計算股價淨值比（P/B Ratio, Price-to-Book）。
    /// </summary>
    /// <param name="price">股價。</param>
    /// <param name="bookValuePerShare">每股股東權益（每股淨值）。</param>
    /// <returns>股淨比 = price / bookValuePerShare。若每股淨值小於等於零則回傳 null。</returns>
    /// <remarks>
    /// 模型：估值倍數模型（Valuation Multiple - P/B Ratio）。
    /// 公式：P/B = Price / Book Value Per Share。
    /// 衡量市場價格相對於每股淨值的倍數。
    /// P/B &lt; 1 可能表示股價低於帳面價值（價值型）。
    /// 金融業與資產密集型產業較適合以 P/B 進行估值。
    /// </remarks>
    public static decimal? CalculatePbRatio(decimal price, decimal bookValuePerShare)
    {
        return bookValuePerShare <= 0 ? null : price / bookValuePerShare;
    }

    /// <summary>
    /// 計算市銷率（P/S Ratio, Price-to-Sales）。
    /// </summary>
    /// <param name="marketCap">總市值。</param>
    /// <param name="revenue">營業收入。</param>
    /// <returns>市銷率 = marketCap / revenue。若營收小於等於零則回傳 null。</returns>
    /// <remarks>
    /// 模型：估值倍數模型（Valuation Multiple - P/S Ratio）。
    /// 公式：P/S = Market Cap / Revenue。
    /// 衡量市場願意為每單位營收付出多少價格。
    /// 適合評估尚在虧損階段但營收持續成長的公司（例如成長型科技股）。
    /// </remarks>
    public static decimal? CalculatePsRatio(decimal marketCap, decimal revenue)
    {
        return revenue <= 0 ? null : marketCap / revenue;
    }

    /// <summary>
    /// 計算企業價值倍數（EV/EBITDA）。
    /// </summary>
    /// <param name="enterpriseValue">企業價值（市值 + 淨負債）。</param>
    /// <param name="ebitda">稅前息前折舊攤銷前盈餘。</param>
    /// <returns>EV/EBITDA = enterpriseValue / ebitda。若 EBITDA 小於等於零則回傳 null。</returns>
    /// <remarks>
    /// 模型：企業價值估值倍數模型（Valuation Multiple - EV/EBITDA）。
    /// 公式：EV/EBITDA = Enterprise Value / EBITDA。
    /// EV = Market Cap + Total Debt - Cash。
    /// 比 P/E 更能反映公司整體價值，因為包含負債並排除折舊攤銷等非現金費用。
    /// 常用於併購估值與跨公司比較，因為排除資本結構影響。
    /// </remarks>
    public static decimal? CalculateEvEbitda(decimal enterpriseValue, decimal ebitda)
    {
        return ebitda <= 0 ? null : enterpriseValue / ebitda;
    }

    /// <summary>
    /// 計算現金殖利率（Dividend Yield）。
    /// </summary>
    /// <param name="annualDividend">年度每股現金股利。</param>
    /// <param name="price">股價。</param>
    /// <returns>殖利率 = annualDividend / price。若股價為零則回傳 null。</returns>
    /// <remarks>
    /// 模型：收益型估值比率模型（Income Ratio - Dividend Yield）。
    /// 公式：Dividend Yield = Annual Dividend / Price。
    /// 衡量投資人每年可從股利獲得的現金報酬率。
    /// 適合評估收息型股票，需搭配股利發放率與公司成長性綜合判斷。
    /// </remarks>
    public static decimal? CalculateDividendYield(decimal annualDividend, decimal price)
    {
        return price == 0 ? null : annualDividend / price;
    }
}
