-- Import demo market prices from CSV into the existing EquityLens EF Core schema.
--
-- Source CSV:
--   data/demo-market-prices-2025-06-03-2026-06-03.csv
--
-- Run from repository root with psql:
--   psql "postgresql://USER:PASSWORD@HOST:PORT/DB_NAME" -f database/postgres/dml/import_demo_market_prices.sql
--
-- Schema alignment:
-- - Inserts/updates instruments in existing table: public.security
-- - Inserts prices into existing table: public.market_price
-- - Does NOT create application tables. EF Core migrations own the schema.
-- - Uses psql client-side \copy, not server-side COPY.
-- - Empty CSV fields are normalized through NULLIF(..., '').

BEGIN;

CREATE TEMP TABLE tmp_market_prices_import (
    ticker          text,
    exchange        text,
    name            text,
    price_date      text,
    price_interval  text,
    open_price      text,
    high_price      text,
    low_price       text,
    close_price     text,
    adjusted_close  text,
    volume          text,
    data_source     text
) ON COMMIT DROP;

\copy tmp_market_prices_import (ticker, exchange, name, price_date, price_interval, open_price, high_price, low_price, close_price, adjusted_close, volume, data_source) FROM 'data/demo-market-prices-2025-06-03-2026-06-03.csv' WITH (FORMAT csv, HEADER true, NULL '');

-- 1) Upsert securities first because market_price.security_id is a FK target.
-- Currency is inferred from exchange for demo data:
-- - TWSE/TPEX -> TWD
-- - otherwise -> USD
-- Asset type is set to ETF for known ETF exchanges in this demo, otherwise Stock.
INSERT INTO public.security (
    ticker,
    exchange,
    name,
    currency,
    asset_type,
    is_active
)
SELECT DISTINCT ON (clean.ticker, clean.exchange)
    clean.ticker,
    clean.exchange,
    clean.name,
    CASE
        WHEN clean.exchange IN ('TWSE', 'TPEX') THEN 'TWD'
        ELSE 'USD'
    END AS currency,
    CASE
        WHEN clean.exchange IN ('AMEX', 'NYSEARCA') THEN 'ETF'
        ELSE 'Stock'
    END AS asset_type,
    true AS is_active
FROM (
    SELECT
        NULLIF(ticker, '') AS ticker,
        NULLIF(exchange, '') AS exchange,
        NULLIF(name, '') AS name
    FROM tmp_market_prices_import
) AS clean
WHERE clean.ticker IS NOT NULL
  AND clean.exchange IS NOT NULL
  AND clean.name IS NOT NULL
ORDER BY clean.ticker, clean.exchange, clean.name
ON CONFLICT (ticker, exchange)
DO UPDATE SET
    name = EXCLUDED.name,
    currency = EXCLUDED.currency,
    asset_type = EXCLUDED.asset_type,
    is_active = true;

-- 2) Insert market prices using the existing EF Core schema.
-- The CSV has date-only bars, while market_price.price_time is timestamptz.
-- We normalize each row to UTC midnight for deterministic daily-bar identity.
--
-- Note: the current EF model has an index on (security_id, interval, price_time),
-- but not a unique constraint. To keep this script idempotent without changing the
-- schema, delete the target slice before inserting it again.
DELETE FROM public.market_price mp
USING public.security s,
      (
          SELECT DISTINCT
              NULLIF(ticker, '') AS ticker,
              NULLIF(exchange, '') AS exchange,
              (NULLIF(price_date, '')::date::timestamp AT TIME ZONE 'UTC') AS price_time,
              NULLIF(price_interval, '') AS interval,
              NULLIF(data_source, '') AS data_source
          FROM tmp_market_prices_import
          WHERE NULLIF(ticker, '') IS NOT NULL
            AND NULLIF(exchange, '') IS NOT NULL
            AND NULLIF(price_date, '') IS NOT NULL
            AND NULLIF(price_interval, '') IS NOT NULL
      ) AS target_rows
WHERE mp.security_id = s.id
  AND s.ticker = target_rows.ticker
  AND s.exchange = target_rows.exchange
  AND mp.price_time = target_rows.price_time
  AND mp.interval = target_rows.interval
  AND COALESCE(mp.data_source, '') = COALESCE(target_rows.data_source, '');

INSERT INTO public.market_price (
    security_id,
    price_time,
    interval,
    open,
    high,
    low,
    close,
    adjusted_close,
    volume,
    data_source
)
SELECT
    s.id AS security_id,
    (NULLIF(src.price_date, '')::date::timestamp AT TIME ZONE 'UTC') AS price_time,
    NULLIF(src.price_interval, '')::varchar(8) AS interval,
    NULLIF(src.open_price, '')::numeric(18, 6) AS open,
    NULLIF(src.high_price, '')::numeric(18, 6) AS high,
    NULLIF(src.low_price, '')::numeric(18, 6) AS low,
    NULLIF(src.close_price, '')::numeric(18, 6) AS close,
    NULLIF(src.adjusted_close, '')::numeric(18, 6) AS adjusted_close,
    NULLIF(src.volume, '')::bigint AS volume,
    NULLIF(src.data_source, '')::varchar(32) AS data_source
FROM tmp_market_prices_import src
JOIN public.security s
  ON s.ticker = NULLIF(src.ticker, '')
 AND s.exchange = NULLIF(src.exchange, '')
WHERE NULLIF(src.ticker, '') IS NOT NULL
  AND NULLIF(src.exchange, '') IS NOT NULL
  AND NULLIF(src.price_date, '') IS NOT NULL
  AND NULLIF(src.price_interval, '') IS NOT NULL
  AND NULLIF(src.open_price, '') IS NOT NULL
  AND NULLIF(src.high_price, '') IS NOT NULL
  AND NULLIF(src.low_price, '') IS NOT NULL
  AND NULLIF(src.close_price, '') IS NOT NULL;

COMMIT;
