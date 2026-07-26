\set ON_ERROR_STOP on
\set factor_version 'tw0050-total-return-v1-20260726'

BEGIN;

DELETE FROM risk_research.price_gap
WHERE factor_version = :'factor_version';
DELETE FROM risk_research.price_adjustment
WHERE factor_version = :'factor_version';
DELETE FROM risk_research.corporate_action
WHERE factor_version = :'factor_version';

\copy risk_research.price_adjustment (security_id,ticker,price_date,raw_close,adjusted_close,adj_factor,factor_source,factor_version,status,status_reason,decision_json) FROM '/tmp/price_adjustment.csv' WITH (FORMAT csv, HEADER true)
\copy risk_research.corporate_action (ticker,event_date,action_type,action_values,yfinance_factor,official_date,official_factor,relative_difference,reconciliation_status,conclusion,source_payload,factor_version) FROM '/tmp/corporate_action_reconciliation.csv' WITH (FORMAT csv, HEADER true)
\copy risk_research.price_gap (ticker,price_date,reason,source,factor_version) FROM '/tmp/price_gap.csv' WITH (FORMAT csv, HEADER true)

COMMIT;

