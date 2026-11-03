# R33 補充題與拆解實驗狀態

未執行付費 investigation，沒有新答案、skill trajectory 或實驗分數。兩題 gold 核實與格式驗證已完成；並不代表系統答對。

- 完整迴圈：待預算覆蓋確認後，以同資料、檢索、來源政策與原八題相同服務設定執行；另報兩題成績及 load_skill 的 id／版本／hash。
- 實驗 A（MaxTurns=1）：尚未執行。已核對 AgentRuntimeRegistration：MaxTurns／MaxRevisionTurns 最低為 2；只能在隔離實驗中允許單輪，原本取證後提交需要後續輪次，不能把無答案與全文取證系統作不公平的能力結論。
- 實驗 C：補充文件引用 A/B 報告的「建議的拆解實驗」，但目前工作樹的 A/B README 未找到實驗 C 的操作定義。未自行猜測要移除來源規則或 skill；待取得具體對照條件再做。

主要阻塞是歷史累計外部費用不完整，無法確認 US$10 剩餘額度，詳見 ../2026-11-03-three-workflows/budget-status.json。依規格跳過新增付費呼叫，繼續離線驗證，不擅自提高預算。
