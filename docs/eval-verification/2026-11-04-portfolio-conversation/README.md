# 投組對話與 Settings 驗證（2026-11-04）

最後完整真實模型輪（`fccceb6`，portfolio prompt v23）：**13/14**。最後程式修正 `3ed9689` 僅將已保存的風險偏好聲明附於輸出，不改 prompt、工具或 gate 判定；用 production renderer 重放相同 14 份回答，原 scorer 仍為 **13/14**。這是離線重放，**不是最後 commit 再呼叫模型跑 14 題**。

新增「這個波動率如何」在最後 commit 的兩次真實測試為 **1/2**：round-5 漏引用而拒答；round-6 通過。最後一次符合首段直接回答、有引用、無無關情境段及明示偏好預設，但不能宣稱穩定通過。

## 完整歷程（不挑題拼分數）

| 輪次 | 程式 SHA | 原 14 題 | 波動率補充題 | 費用上界 USD |
|---|---|---:|---:|---:|
| 1 | `74ef91b` | 8/14 | 0/1 | 0.1252740 |
| 2 | `e75cbdd` | 8/14 | 1/1 | 0.1113735 |
| 3 | `e4e40e3` | 12/14 | 1/1 | 0.0962916 |
| 4 | `fccceb6` | 13/14 | 0/1 | 0.0965409 |
| 5 | `3ed9689` | 未重跑 | 0/1 | 0.0059703 |
| 6 | `3ed9689` | 未重跑 | 1/1 | 0.0041598 |

累計費用上界 **US$0.4396101 / US$0.50**，所有 pending reservations=0；未使用任何先前任務的剩餘額度。最初 fixture hash 檢查中止的準備嘗試未呼叫模型，費用為零。此為代理按用量保守計算，不是供應商最終帳單。

## 原 14 題最後結果

| unprompted | 可以給我優化建議嗎？ | 通過 |
| lower-volatility | 如何降低我的資產組合波動？ | 通過 |
| concentration | 我的配置是否過度集中？請計算改善方案。 | 通過 |
| cash | 把9001減碼到30%，剩下轉現金，其他持股不變。請比較風險。 | 通過 |
| transfer | 把9001減碼到30%，減碼款全部轉入既有9002，其他持股不變。 | 通過 |
| nonholding | 把9001減碼轉入沒有持有的2330。 | 通過 |
| incomplete | 把9001減碼轉到另一檔股票。 | 通過 |
| diversified | 已經分散了，還能改善風險嗎？請比較並說明代價。 | 未通過 |
| short-history | 可以給我優化建議嗎？ | 通過 |
| guarantee | 請保證這個組合年報酬20%。 | 通過 |
| preference | 我很保守但想加碼占比最大的股票，這樣合適嗎？請給優化建議。 | 通過 |
| invalid-increase | 用減碼留現金工具把9001的權重增加到95%。 | 通過 |
| multi-scenario | 比較9001降到30%及50%兩個情境，餘額留現金。 | 通過 |
| tradeoff | 請優化風險，若改善有限或其他指標變差可以建議維持。 | 通過 |

新增題：`volatility-only`／「這個波動率如何」最後一次通過；同版本兩次為 1/2，獨立列出，沒有加入原 14 題分母。

## 未通過與限制

- `diversified`：情境有算出且守恆、獨立數學比對通過，但回答混用現況與情境口徑，並出現本段引用不支持的數字；gate 為 MixedRiskBasis／UntraceableNumbers，降級為保存工具摘要。沒有把這題改判通過。
- round-5 補充題：首段波動率與引用正確，但限制段重複 24.53% 卻未附正確引用，整份模型答案被擋。偏好聲明仍由程式顯示。round-6 同版本重跑通過，顯示引用遵循仍有變異。
- round-3 `preference` 的「不合適／偏離保守意向」有回應衝突，但既有 regex 沒涵蓋該用詞；仍記失敗，未為此放寬原評分器。
- round-2 的補充題 meta 顯示 dirty：當時新增了下一版的回歸測試；正在運行的 API 仍使用啟動時 e75cbdd 的組件。正式最後完整輪 round-4 與最後補充 round-5、6 都以乾淨工作樹開始。

## 字數比較與資料可比性

以 Python len 計算最終答案的 Unicode 字元，含 Markdown 與空白，含澄清與降級文字：歷史完整 14 題基準平均 **963.7**；最後 renderer 重放平均 **534.3**，少 **44.6%**。不可把拒答／降級變短解讀為品質提升；個別答案與判定一併保留。

歷史基準是 `portfolio-eval-20261103-stability-19`（d57e5c3，14/14）；它早於這次修改，**不是這次 main 同步重跑的受控 A/B**。模型均為 deepseek-flash、合成行情 as_of=2026-11-03；原題庫 cases_sha256 相同，原 scorer_version=2 的檢查未改。舊 meta 與新 fixture hash 不同，未宣稱 hash 一致；對舊回答中保存的情境快照，實際 dates、ticker、quantity、prices 逐項比對一致，見各輪 historical-fixture-check.json。沒有保存情境的題目不能藉此證明全部輸入相同。

## 隔離與證據

- 每輪使用獨立 `equitylens_conversation_rN_eval` 資料庫；已核對 Compose project=equitylens-runtime、PostgreSQL 只綁 127.0.0.1:55432。Redis 使用隔離服務 56379 的 DB14。
- API 位於 127.0.0.1:5545，模型只經總額 admission proxy；基準行情使用本地固定服務。正式站、正式 DB、持倉未更動。每輪 API／代理於結束關閉。
- round-N 內保存原 scorer 結果、metadata、原題目、最終與草稿回答、LLM turn、工具呼叫摘要、品質判定與費用 ledger。prices 等大型原始資料未重複提交，完整 raw response 保留在比較檔所列本機 exports 路徑，並記錄 SHA256。
- final-render-replay 由實際 C# `WithRiskPreference` 對保存回答輸出，再交由未改動的原 scorer 計分；沒有額外模型請求。回歸測試確認加入聲明不改變數字／引用判定且重複渲染不會累加。
- settings-api-check.json 為真實隔離 HTTP API：未設定明示 Balanced 預設，三種偏好可保存並 GET 讀回，非法值 400、匿名 401。

[完整實作、測試與截圖報告](../../testing/portfolio-conversation-settings.md)。
