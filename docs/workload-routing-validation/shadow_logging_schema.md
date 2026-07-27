# Workload routing shadow logging schema

Shadow 只記錄與評估，使用者輸出仍固定採 production MVEWMA。

```text
risk_routing_shadow_event
  run_id                       text primary key
  forecast_time                timestamptz
  portfolio_id_hash            text
  workload_hash                text
  realized_return_date         date null
  realized_return              double precision null

  production_model             text  -- always MVEWMA during shadow
  routing_recommendation       text
  routing_selected_model       text
  routing_rule_version         text
  routing_manifest_sha256      text

  mvewma_var_95                double precision
  mvewma_es_95                 double precision
  mvewma_var_99                double precision
  mvewma_es_99                 double precision
  garch_var_95                 double precision null
  garch_es_95                  double precision null
  garch_var_99                 double precision null
  garch_es_99                  double precision null

  mvewma_ql_95                 double precision null
  mvewma_fz0_95                double precision null
  mvewma_breach_95             boolean null
  garch_ql_95                  double precision null
  garch_fz0_95                 double precision null
  garch_breach_95              boolean null
  -- 同樣保存 99%

  mvewma_runtime_ms             double precision
  garch_runtime_ms              double precision null
  requested_model              text
  fallback_selected_model      text
  fallback_reason_code         text null
  fallback_depth               integer
  fit_health_json              jsonb
  data_quality_status          text

  nominal_asset_count          integer
  hhi                          double precision
  effective_assets             double precision
  maximum_weight               double precision
  average_pairwise_correlation double precision
  first_pc_explained           double precision
  sector_concentration         double precision

  model_version_mvewma         text
  model_version_garch          text
  data_factor_version          text
  input_payload_sha256         text
  created_at                   timestamptz
```

## 約束

- `run_id` 由 manifest hash、input hash、forecast time 確定性衍生。
- Shadow failure 不得改變 production response。
- Realized return 到期後 append outcome 欄位；不得覆寫原 prediction。
- 原始個人識別資訊與持股明細不放入分析事件；使用不可逆 workload hash。
- 監控 fit failures、fallback depth、runtime、coverage、QL/FZ0、switching
  與 VaR/ES model gap。

