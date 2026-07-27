CREATE SCHEMA IF NOT EXISTS risk_research;

CREATE TABLE IF NOT EXISTS risk_research.corporate_action (
    ticker text NOT NULL,
    event_date date NOT NULL,
    action_type text NOT NULL,
    action_values jsonb NOT NULL,
    yfinance_factor numeric(30, 12),
    official_date date,
    official_factor numeric(30, 12),
    relative_difference numeric(30, 12),
    reconciliation_status text NOT NULL,
    conclusion text NOT NULL,
    source_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    factor_version text NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (ticker, event_date, action_type, factor_version)
);

CREATE TABLE IF NOT EXISTS risk_research.price_adjustment (
    security_id uuid NOT NULL,
    ticker text NOT NULL,
    price_date date NOT NULL,
    raw_close numeric(30, 12),
    adjusted_close numeric(30, 12),
    adj_factor numeric(30, 12),
    factor_source text NOT NULL,
    factor_version text NOT NULL,
    status text NOT NULL,
    status_reason text,
    decision_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (ticker, price_date, factor_version)
);

CREATE INDEX IF NOT EXISTS ix_price_adjustment_security_date
    ON risk_research.price_adjustment (security_id, price_date);

CREATE TABLE IF NOT EXISTS risk_research.price_gap (
    ticker text NOT NULL,
    price_date date NOT NULL,
    reason text NOT NULL,
    source text NOT NULL,
    factor_version text NOT NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (ticker, price_date, factor_version)
);

COMMENT ON SCHEMA risk_research IS
    'Research-only adjusted-close pipeline; production market_price remains read-only.';
