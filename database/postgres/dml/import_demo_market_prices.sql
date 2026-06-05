-- Import demo market prices from CSV into PostgreSQL.
--
-- Source CSV:
--   data/demo-market-prices-2025-06-03-2026-06-03.csv
--
-- Run from repository root with psql:
--   psql "postgresql://USER:PASSWORD@HOST:PORT/DB_NAME" -f database/postgres/dml/import_demo_market_prices.sql
--
-- Notes:
-- - This script uses psql's client-side \copy, not server-side COPY.
-- - CSV column `interval` is mapped to `bar_interval` to avoid keyword/type ambiguity.
-- - Empty numeric fields are normalized with NULLIF(..., '').

BEGIN;

CREATE TABLE IF NOT EXISTS public.market_prices (
    ticker          text           NOT NULL,
    exchange        text           NOT NULL,
    name            text           NOT NULL,
    price_date      date           NOT NULL,
    bar_interval    text           NOT NULL,
    open_price      numeric(18, 6) NULL,
    high_price      numeric(18, 6) NULL,
    low_price       numeric(18, 6) NULL,
    close_price     numeric(18, 6) NULL,
    adjusted_close  numeric(18, 6) NULL,
    volume          bigint         NULL,
    data_source     text           NOT NULL,
    created_at      timestamptz    NOT NULL DEFAULT now(),
    updated_at      timestamptz    NOT NULL DEFAULT now(),

    CONSTRAINT pk_market_prices
        PRIMARY KEY (ticker, exchange, price_date, bar_interval, data_source)
);

CREATE TEMP TABLE tmp_market_prices_import (
    ticker          text,
    exchange        text,
    name            text,
    price_date      text,
    bar_interval    text,
    open_price      text,
    high_price      text,
    low_price       text,
    close_price     text,
    adjusted_close  text,
    volume          text,
    data_source     text
) ON COMMIT DROP;

\copy tmp_market_prices_import (ticker, exchange, name, price_date, bar_interval, open_price, high_price, low_price, close_price, adjusted_close, volume, data_source) FROM 'data/demo-market-prices-2025-06-03-2026-06-03.csv' WITH (FORMAT csv, HEADER true, NULL '');

INSERT INTO public.market_prices (
    ticker,
    exchange,
    name,
    price_date,
    bar_interval,
    open_price,
    high_price,
    low_price,
    close_price,
    adjusted_close,
    volume,
    data_source
)
SELECT
    NULLIF(ticker, '')::text,
    NULLIF(exchange, '')::text,
    NULLIF(name, '')::text,
    NULLIF(price_date, '')::date,
    NULLIF(bar_interval, '')::text,
    NULLIF(open_price, '')::numeric(18, 6),
    NULLIF(high_price, '')::numeric(18, 6),
    NULLIF(low_price, '')::numeric(18, 6),
    NULLIF(close_price, '')::numeric(18, 6),
    NULLIF(adjusted_close, '')::numeric(18, 6),
    NULLIF(volume, '')::bigint,
    NULLIF(data_source, '')::text
FROM tmp_market_prices_import
WHERE NULLIF(ticker, '') IS NOT NULL
  AND NULLIF(exchange, '') IS NOT NULL
  AND NULLIF(price_date, '') IS NOT NULL
  AND NULLIF(bar_interval, '') IS NOT NULL
  AND NULLIF(data_source, '') IS NOT NULL
ON CONFLICT (ticker, exchange, price_date, bar_interval, data_source)
DO UPDATE SET
    name           = EXCLUDED.name,
    open_price     = EXCLUDED.open_price,
    high_price     = EXCLUDED.high_price,
    low_price      = EXCLUDED.low_price,
    close_price    = EXCLUDED.close_price,
    adjusted_close = EXCLUDED.adjusted_close,
    volume         = EXCLUDED.volume,
    updated_at     = now();

COMMIT;
