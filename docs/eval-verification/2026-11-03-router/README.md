# Router 題庫與程式評分（R36）

26 題，涵蓋研究、投組、情境、混合、模糊、越權、沒有投組、不支援情境、直接聊天、追問與明確入口。每題保存訊息、授權投組與持股、入口／前次 run、預期 action／workflow／關鍵參數。測試 ID 全為合成值。

計分輸入為最終路由決策（含程式驗證），格式 `{id, action, workflow, params}`。追問／明確入口亦納入，才能檢查零次分類路徑。混合題先處理使用者指定的第一個角色；不在單一 run 跨角色。

```bash
cd evals
python -m equitylens_eval.router_eval --cases cases/router_v1.jsonl \
  --predictions /path/to/captured-router-decisions.jsonl --out /path/to/new-router-report
```

程式以 action／workflow 等值及關鍵參數子集合檢查，輸出逐題結果、準確率與兩個混淆矩陣；不使用 LLM judge。多出的預測 ID／重複 ID 拒絕，缺測題在部分執行時以失敗計；完全未執行時準確率為 null。

**本次未執行真實 Router 分類**：歷史外部 API 累計費用沒有完整覆蓋，不能確認 US$10 剩餘預算。summary.json 明確記錄 not_run、0/26 observed、accuracy:null，混淆矩陣為空。不能拿單元測試的正確率宣稱 Router 模型準確率。預算證據見 ../2026-11-03-three-workflows/budget-status.json。

恢復驗收時需在隔離服務建立對應測試投組／資料，再保存真正的路由回應（必要時以穩定 fixture ID 映射實際 GUID），執行上述評分；不要將 expected 欄位複製成預測。
