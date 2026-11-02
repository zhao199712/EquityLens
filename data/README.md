# Data

此目錄只存放可公開、可重現的示範資料與開發測試資料。

- `demo/`：用於本機開發、展示與測試的資料集。
- Production data、下載的原始檔與大型暫存輸出不應提交到 Git。

目前的 demo market price CSV 可搭配 `database/postgres/scripts/import-demo-market-prices.sql` 匯入本機 PostgreSQL。
