# EquityLens

## TWSE 財報爬蟲 PoC 驗證

### 驗證項目與結果

| 步驟 | 結果 | 說明 |
|------|------|------|
| TPEX 0050 成分股 API | ✅ | `https://www.tpex.org.tw/web/stock/iNdex_info/gretai50/ingrid/r50cnstnt_result.php?l=zh-tw&o=data`，CSV 格式，共 50 檔 |
| TWSE 財報查詢頁面解析 | ✅ | `https://doc.twse.com.tw/server-java/t57sb01?step=1&colorchg=1&co_id={code}&year={year}&mtype=A&`，HTML 表格可解析 |
| TWSE PDF 下載 | ✅ | POST 表單到同一 URL（step=9, kind=A），回應含 PDF 連結，下載成功 |
| PdfPig 文字提取 | ✅ | 93 頁台積電 Q1 財報成功提取中文，`PdfPig 0.1.14` NuGet 套件 |

### TWSE URL 結構

- **財報查詢頁面**: `GET https://doc.twse.com.tw/server-java/t57sb01?step=1&colorchg=1&co_id={code}&year={year}&mtype=A&`
- **PDF 下載**: `POST` 同一 URL，body: `step=9&kind=A&co_id={code}&filename={pdf_name}`
- **PDF 實際連結**: 回應 HTML 中包含 `/pdf/{filename}_{timestamp}.pdf`

### 0050 成分股

- **資料來源**: TPEX CSV API
- **更新頻率**: 每日
- **股票數量**: 50 檔

### API 端點

```
POST /api/financial-filings/crawl
Authorization: Bearer {token}
Content-Type: application/json

{
  "stockCodes": ["2330", "2498"],
  "startYear": 112,
  "endYear": 115
}
```

### 回應格式

```json
{
  "totalRequested": 8,
  "successCount": 6,
  "failedCount": 2,
  "failedFiles": [
    { "stockCode": "2498", "year": 112, "reason": "..." }
  ]
}
```
