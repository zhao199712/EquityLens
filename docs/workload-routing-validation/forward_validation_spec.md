# Workload routing forward-validation 規格

## 固定基準

- Routing manifest：
  `workload-routing-v1-20260726`
- Manifest SHA-256：
  `90b328d5d9b07aaa2bb7a61c836f2db0605ed155f5fdc30649f5f2a4d9f0cf6f`
- 起始 cutoff：2026-07-16。
- 後續資料只能 append；manifest、門檻、seed derivation、loss 與模型公式
  不得回改。

## 樣本門檻

- 250 個新增共同有效交易日：初步檢查，不 promotion。
- 500 個新增共同有效交易日：正式 promotion review。
- 或在較短期間內涵蓋至少兩個事前登記的壓力事件；事件定義必須在發生前
  寫入下一版只增不改的 event registry。

## 每日保存

每個 workload 同時保存兩模型的 predictive paths 摘要、VaR、ES、實現
return、QL、FZ0、breach、runtime、fit health、fallback、HHI、N_eff、
correlation features、model version 與 factor version。

## 判讀

- 使用 workload 為 cluster 的 paired bootstrap。
- 同時報告全域策略、固定 routing、breadth/HHI strata 與 worst-case
  regret。
- 不以 250 日初檢結果調整門檻；若要改規則，建立新 manifest，舊規則
  仍保留為獨立 comparator，重新累積 holdout。

