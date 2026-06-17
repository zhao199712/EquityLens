CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE app_user (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        email character varying(256) NOT NULL,
        display_name character varying(128) NOT NULL,
        password_hash character varying(512) NOT NULL,
        role character varying(32) NOT NULL DEFAULT 'User',
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_app_user" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE risk_model_setting (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        name character varying(64) NOT NULL,
        lookback_days integer NOT NULL DEFAULT 252,
        confidence_level numeric(5,4) NOT NULL DEFAULT 0.95,
        holding_period_days integer NOT NULL DEFAULT 1,
        method character varying(32) NOT NULL DEFAULT 'Historical',
        return_type character varying(16) NOT NULL DEFAULT 'Log',
        is_default boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_risk_model_setting" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE scenario (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        name character varying(128) NOT NULL,
        description character varying(512),
        shock_type character varying(16) NOT NULL,
        shock_value numeric(18,6) NOT NULL,
        target_type character varying(32) NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        CONSTRAINT "PK_scenario" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE security (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        ticker character varying(16) NOT NULL,
        exchange character varying(16) NOT NULL,
        name character varying(256) NOT NULL,
        asset_type character varying(32),
        currency character varying(3) NOT NULL DEFAULT 'USD',
        isin character varying(12),
        sector character varying(64),
        industry character varying(64),
        is_active boolean NOT NULL DEFAULT TRUE,
        CONSTRAINT "PK_security" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE portfolio (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        owner_user_id uuid NOT NULL,
        name character varying(128) NOT NULL,
        description character varying(512),
        base_currency character varying(3) NOT NULL DEFAULT 'USD',
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_portfolio" PRIMARY KEY (id),
        CONSTRAINT "FK_portfolio_app_user_owner_user_id" FOREIGN KEY (owner_user_id) REFERENCES app_user (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE uploaded_file (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        uploaded_by_user_id uuid NOT NULL,
        bucket_name character varying(128) NOT NULL,
        object_key character varying(512) NOT NULL,
        original_file_name character varying(256) NOT NULL,
        content_type character varying(128),
        file_size_bytes bigint NOT NULL,
        sha256_hash character varying(64),
        storage_provider character varying(32) NOT NULL DEFAULT 'MinIO',
        upload_status character varying(16) NOT NULL DEFAULT 'Pending',
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_uploaded_file" PRIMARY KEY (id),
        CONSTRAINT "FK_uploaded_file_app_user_uploaded_by_user_id" FOREIGN KEY (uploaded_by_user_id) REFERENCES app_user (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE financial_statement (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        security_id uuid NOT NULL,
        statement_type character varying(32) NOT NULL,
        period_type character varying(16) NOT NULL,
        fiscal_year integer NOT NULL,
        fiscal_quarter integer,
        period_end_date date NOT NULL,
        published_date date,
        currency character varying(3) NOT NULL DEFAULT 'USD',
        data_source character varying(32),
        CONSTRAINT "PK_financial_statement" PRIMARY KEY (id),
        CONSTRAINT "FK_financial_statement_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE market_price (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        security_id uuid NOT NULL,
        price_time timestamp with time zone NOT NULL,
        interval character varying(8) NOT NULL DEFAULT '1d',
        open numeric(18,6) NOT NULL,
        high numeric(18,6) NOT NULL,
        low numeric(18,6) NOT NULL,
        close numeric(18,6) NOT NULL,
        adjusted_close numeric(18,6),
        volume bigint,
        data_source character varying(32),
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_market_price" PRIMARY KEY (id),
        CONSTRAINT "FK_market_price_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE financial_report (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        portfolio_id uuid NOT NULL,
        title character varying(256) NOT NULL,
        as_of_date date NOT NULL,
        status character varying(16) NOT NULL DEFAULT 'Draft',
        summary text,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_financial_report" PRIMARY KEY (id),
        CONSTRAINT "FK_financial_report_portfolio_portfolio_id" FOREIGN KEY (portfolio_id) REFERENCES portfolio (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE portfolio_holding (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        portfolio_id uuid NOT NULL,
        security_id uuid NOT NULL,
        quantity numeric(18,6) NOT NULL,
        average_cost numeric(18,6) NOT NULL,
        cost_currency character varying(3) NOT NULL DEFAULT 'USD',
        note character varying(512),
        updated_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_portfolio_holding" PRIMARY KEY (id),
        CONSTRAINT "FK_portfolio_holding_portfolio_portfolio_id" FOREIGN KEY (portfolio_id) REFERENCES portfolio (id) ON DELETE CASCADE,
        CONSTRAINT "FK_portfolio_holding_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE portfolio_snapshot (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        portfolio_id uuid NOT NULL,
        snapshot_date date NOT NULL,
        market_value numeric(18,4) NOT NULL,
        cost_value numeric(18,4) NOT NULL,
        unrealized_pnl numeric(18,4) NOT NULL,
        daily_return numeric(10,6),
        currency character varying(3) NOT NULL DEFAULT 'USD',
        CONSTRAINT "PK_portfolio_snapshot" PRIMARY KEY (id),
        CONSTRAINT "FK_portfolio_snapshot_portfolio_portfolio_id" FOREIGN KEY (portfolio_id) REFERENCES portfolio (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE risk_run (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        portfolio_id uuid NOT NULL,
        risk_model_setting_id uuid NOT NULL,
        run_name character varying(128),
        as_of_date date NOT NULL,
        status character varying(16) NOT NULL DEFAULT 'Pending',
        started_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        error_message text,
        input_hash character varying(64),
        CONSTRAINT "PK_risk_run" PRIMARY KEY (id),
        CONSTRAINT "FK_risk_run_portfolio_portfolio_id" FOREIGN KEY (portfolio_id) REFERENCES portfolio (id) ON DELETE CASCADE,
        CONSTRAINT "FK_risk_run_risk_model_setting_risk_model_setting_id" FOREIGN KEY (risk_model_setting_id) REFERENCES risk_model_setting (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE document (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        uploaded_file_id uuid NOT NULL,
        title character varying(256) NOT NULL,
        document_type character varying(32),
        source_url character varying(1024),
        language character varying(8) NOT NULL DEFAULT 'en',
        published_at timestamp with time zone,
        parsed_at_utc timestamp with time zone,
        parse_status character varying(16) NOT NULL DEFAULT 'Pending',
        CONSTRAINT "PK_document" PRIMARY KEY (id),
        CONSTRAINT "FK_document_uploaded_file_uploaded_file_id" FOREIGN KEY (uploaded_file_id) REFERENCES uploaded_file (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE financial_line_item (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        financial_statement_id uuid NOT NULL,
        code character varying(32) NOT NULL,
        name character varying(128) NOT NULL,
        amount numeric(18,4) NOT NULL,
        unit character varying(16),
        CONSTRAINT "PK_financial_line_item" PRIMARY KEY (id),
        CONSTRAINT "FK_financial_line_item_financial_statement_financial_statement~" FOREIGN KEY (financial_statement_id) REFERENCES financial_statement (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE ai_memo (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        risk_run_id uuid,
        financial_report_id uuid,
        title character varying(256) NOT NULL,
        content text NOT NULL,
        model_name character varying(64),
        prompt_version character varying(32),
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_ai_memo" PRIMARY KEY (id),
        CONSTRAINT "FK_ai_memo_financial_report_financial_report_id" FOREIGN KEY (financial_report_id) REFERENCES financial_report (id) ON DELETE SET NULL,
        CONSTRAINT "FK_ai_memo_risk_run_risk_run_id" FOREIGN KEY (risk_run_id) REFERENCES risk_run (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE job_run (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        created_by_user_id uuid,
        risk_run_id uuid,
        financial_report_id uuid,
        uploaded_file_id uuid,
        job_type character varying(32) NOT NULL,
        status character varying(16) NOT NULL DEFAULT 'Queued',
        progress_percent integer NOT NULL DEFAULT 0,
        redis_job_id character varying(64),
        correlation_id character varying(64),
        payload_json jsonb,
        result_json jsonb,
        started_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        error_message text,
        CONSTRAINT "PK_job_run" PRIMARY KEY (id),
        CONSTRAINT "FK_job_run_app_user_created_by_user_id" FOREIGN KEY (created_by_user_id) REFERENCES app_user (id) ON DELETE SET NULL,
        CONSTRAINT "FK_job_run_financial_report_financial_report_id" FOREIGN KEY (financial_report_id) REFERENCES financial_report (id) ON DELETE SET NULL,
        CONSTRAINT "FK_job_run_risk_run_risk_run_id" FOREIGN KEY (risk_run_id) REFERENCES risk_run (id) ON DELETE SET NULL,
        CONSTRAINT "FK_job_run_uploaded_file_uploaded_file_id" FOREIGN KEY (uploaded_file_id) REFERENCES uploaded_file (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE risk_metric (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        risk_run_id uuid NOT NULL,
        metric_name character varying(32) NOT NULL,
        metric_value numeric(18,6) NOT NULL,
        unit character varying(16),
        confidence_level numeric(5,4),
        holding_period_days integer,
        CONSTRAINT "PK_risk_metric" PRIMARY KEY (id),
        CONSTRAINT "FK_risk_metric_risk_run_risk_run_id" FOREIGN KEY (risk_run_id) REFERENCES risk_run (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE scenario_result (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        risk_run_id uuid NOT NULL,
        scenario_id uuid NOT NULL,
        portfolio_value_before numeric(18,4) NOT NULL,
        portfolio_value_after numeric(18,4) NOT NULL,
        pnl_amount numeric(18,4) NOT NULL,
        pnl_percent numeric(10,6) NOT NULL,
        CONSTRAINT "PK_scenario_result" PRIMARY KEY (id),
        CONSTRAINT "FK_scenario_result_risk_run_risk_run_id" FOREIGN KEY (risk_run_id) REFERENCES risk_run (id) ON DELETE CASCADE,
        CONSTRAINT "FK_scenario_result_scenario_scenario_id" FOREIGN KEY (scenario_id) REFERENCES scenario (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE document_chunk (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        document_id uuid NOT NULL,
        chunk_index integer NOT NULL,
        content text NOT NULL,
        token_count integer,
        page_number integer,
        section_title character varying(256),
        content_hash character varying(64),
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_document_chunk" PRIMARY KEY (id),
        CONSTRAINT "FK_document_chunk_document_document_id" FOREIGN KEY (document_id) REFERENCES document (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE critic_note (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        ai_memo_id uuid NOT NULL,
        reviewer_type character varying(16) NOT NULL,
        reviewer_name character varying(64),
        severity character varying(16) NOT NULL DEFAULT 'Info',
        comment text NOT NULL,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_critic_note" PRIMARY KEY (id),
        CONSTRAINT "FK_critic_note_ai_memo_ai_memo_id" FOREIGN KEY (ai_memo_id) REFERENCES ai_memo (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE citation (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        ai_memo_id uuid NOT NULL,
        document_chunk_id uuid,
        source_type character varying(32),
        source_title character varying(256),
        source_url character varying(1024),
        reference_key character varying(64),
        quote_text text,
        relevance_score numeric(5,4),
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_citation" PRIMARY KEY (id),
        CONSTRAINT "FK_citation_ai_memo_ai_memo_id" FOREIGN KEY (ai_memo_id) REFERENCES ai_memo (id) ON DELETE CASCADE,
        CONSTRAINT "FK_citation_document_chunk_document_chunk_id" FOREIGN KEY (document_chunk_id) REFERENCES document_chunk (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE TABLE document_embedding (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        document_chunk_id uuid NOT NULL,
        embedding vector(1536) NOT NULL,
        embedding_model character varying(64) NOT NULL,
        dimensions integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_document_embedding" PRIMARY KEY (id),
        CONSTRAINT "FK_document_embedding_document_chunk_document_chunk_id" FOREIGN KEY (document_chunk_id) REFERENCES document_chunk (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_ai_memo_financial_report_id" ON ai_memo (financial_report_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_ai_memo_risk_run_id" ON ai_memo (risk_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_app_user_email" ON app_user (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_citation_ai_memo_id" ON citation (ai_memo_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_citation_document_chunk_id" ON citation (document_chunk_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_critic_note_ai_memo_id" ON critic_note (ai_memo_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_document_uploaded_file_id" ON document (uploaded_file_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_document_chunk_document_id_chunk_index" ON document_chunk (document_id, chunk_index);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_document_embedding_document_chunk_id" ON document_embedding (document_chunk_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_financial_line_item_financial_statement_id_code" ON financial_line_item (financial_statement_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_financial_report_portfolio_id" ON financial_report (portfolio_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_financial_statement_security_id_statement_type_period_type_~" ON financial_statement (security_id, statement_type, period_type, fiscal_year, fiscal_quarter);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_job_run_created_by_user_id" ON job_run (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_job_run_financial_report_id" ON job_run (financial_report_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_job_run_risk_run_id" ON job_run (risk_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_job_run_uploaded_file_id" ON job_run (uploaded_file_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_market_price_security_id_interval_price_time" ON market_price (security_id, interval, price_time);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_portfolio_owner_user_id" ON portfolio (owner_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_portfolio_holding_portfolio_id" ON portfolio_holding (portfolio_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_portfolio_holding_security_id" ON portfolio_holding (security_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_portfolio_snapshot_portfolio_id_snapshot_date" ON portfolio_snapshot (portfolio_id, snapshot_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_risk_metric_risk_run_id" ON risk_metric (risk_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_risk_run_portfolio_id" ON risk_run (portfolio_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_risk_run_risk_model_setting_id" ON risk_run (risk_model_setting_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_scenario_result_risk_run_id" ON scenario_result (risk_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_scenario_result_scenario_id" ON scenario_result (scenario_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_security_ticker_exchange" ON security (ticker, exchange);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_uploaded_file_object_key" ON uploaded_file (object_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    CREATE INDEX "IX_uploaded_file_uploaded_by_user_id" ON uploaded_file (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260602103820_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260602103820_InitialCreate', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    DROP INDEX "IX_market_price_security_id_interval_price_time";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    ALTER TABLE security ADD metadata_source character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    ALTER TABLE security ADD metadata_updated_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    ALTER TABLE security ADD prices_source character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    ALTER TABLE security ADD prices_synced_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    ALTER TABLE market_price ADD updated_at_utc timestamp with time zone NOT NULL DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    CREATE UNIQUE INDEX "IX_market_price_security_id_interval_price_time" ON market_price (security_id, interval, price_time);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260608081354_AddMetadataAndPriceUpdatedAt') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260608081354_AddMetadataAndPriceUpdatedAt', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260609112744_AddRefreshTokens') THEN
    CREATE TABLE refresh_token (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        user_id uuid NOT NULL,
        token character varying(512) NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        created_by_ip character varying(45),
        user_agent character varying(512),
        is_revoked boolean NOT NULL DEFAULT FALSE,
        is_used boolean NOT NULL DEFAULT FALSE,
        CONSTRAINT "PK_refresh_token" PRIMARY KEY (id),
        CONSTRAINT "FK_refresh_token_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260609112744_AddRefreshTokens') THEN
    CREATE UNIQUE INDEX "IX_refresh_token_token" ON refresh_token (token);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260609112744_AddRefreshTokens') THEN
    CREATE INDEX "IX_refresh_token_user_id" ON refresh_token (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260609112744_AddRefreshTokens') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260609112744_AddRefreshTokens', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072424_AddExchangeRates') THEN
    CREATE TABLE exchange_rate (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        source_currency character varying(3) NOT NULL,
        target_currency character varying(3) NOT NULL,
        rate numeric(18,6) NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_exchange_rate" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072424_AddExchangeRates') THEN
    CREATE UNIQUE INDEX "IX_exchange_rate_source_currency_target_currency" ON exchange_rate (source_currency, target_currency);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072424_AddExchangeRates') THEN
    INSERT INTO exchange_rate (id, source_currency, target_currency, rate, updated_at_utc)
    VALUES ('46751faa-de54-4adc-b2c0-81ef26e2ad90', 'USD', 'TWD', 30.0, TIMESTAMPTZ '2026-01-01T00:00:00Z');
    INSERT INTO exchange_rate (id, source_currency, target_currency, rate, updated_at_utc)
    VALUES ('c3b9f715-1b02-4661-bd7f-99db225e3a33', 'TWD', 'USD', 0.033333, TIMESTAMPTZ '2026-01-01T00:00:00Z');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072424_AddExchangeRates') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260610072424_AddExchangeRates', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072752_UpdatePortfolioDefaultCurrency') THEN
    ALTER TABLE portfolio ALTER COLUMN base_currency SET DEFAULT 'TWD';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610072752_UpdatePortfolioDefaultCurrency') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260610072752_UpdatePortfolioDefaultCurrency', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    DROP INDEX "IX_portfolio_holding_portfolio_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    CREATE TABLE portfolio_transaction (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        portfolio_id uuid NOT NULL,
        security_id uuid NOT NULL,
        transaction_type character varying(4) NOT NULL,
        quantity numeric(18,6) NOT NULL,
        price numeric(18,6) NOT NULL,
        fee numeric(18,6) NOT NULL DEFAULT 0.0,
        transaction_date date NOT NULL,
        note character varying(512),
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_portfolio_transaction" PRIMARY KEY (id),
        CONSTRAINT "FK_portfolio_transaction_portfolio_portfolio_id" FOREIGN KEY (portfolio_id) REFERENCES portfolio (id) ON DELETE CASCADE,
        CONSTRAINT "FK_portfolio_transaction_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    CREATE UNIQUE INDEX "IX_portfolio_holding_portfolio_id_security_id" ON portfolio_holding (portfolio_id, security_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    CREATE INDEX "IX_portfolio_transaction_portfolio_id_security_id_transaction_~" ON portfolio_transaction (portfolio_id, security_id, transaction_date, transaction_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    CREATE INDEX "IX_portfolio_transaction_security_id" ON portfolio_transaction (security_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610090534_AddPortfolioTransactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260610090534_AddPortfolioTransactions', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    CREATE TABLE financial_filing (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        security_id uuid NOT NULL,
        uploaded_file_id uuid NOT NULL,
        document_id uuid,
        uploaded_by_user_id uuid NOT NULL,
        filing_type character varying(32) NOT NULL,
        fiscal_year integer NOT NULL,
        fiscal_quarter integer,
        period_end_date date,
        published_at timestamp with time zone,
        language character varying(8) NOT NULL DEFAULT 'en',
        currency character varying(3),
        source character varying(64),
        source_url character varying(1024),
        parse_status character varying(16) NOT NULL DEFAULT 'Pending',
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_financial_filing" PRIMARY KEY (id),
        CONSTRAINT "FK_financial_filing_app_user_uploaded_by_user_id" FOREIGN KEY (uploaded_by_user_id) REFERENCES app_user (id) ON DELETE CASCADE,
        CONSTRAINT "FK_financial_filing_document_document_id" FOREIGN KEY (document_id) REFERENCES document (id) ON DELETE SET NULL,
        CONSTRAINT "FK_financial_filing_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE CASCADE,
        CONSTRAINT "FK_financial_filing_uploaded_file_uploaded_file_id" FOREIGN KEY (uploaded_file_id) REFERENCES uploaded_file (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    CREATE INDEX "IX_financial_filing_document_id" ON financial_filing (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    CREATE INDEX "IX_financial_filing_security_id_fiscal_year_fiscal_quarter_fil~" ON financial_filing (security_id, fiscal_year, fiscal_quarter, filing_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    CREATE INDEX "IX_financial_filing_uploaded_by_user_id" ON financial_filing (uploaded_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    CREATE INDEX "IX_financial_filing_uploaded_file_id" ON financial_filing (uploaded_file_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260610100116_AddFinancialFiling') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260610100116_AddFinancialFiling', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617094804_AddInvestorConference') THEN
    CREATE TABLE investor_conference (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        security_id uuid NOT NULL,
        uploaded_file_id uuid,
        document_id uuid,
        title character varying(256) NOT NULL,
        event_date timestamp with time zone,
        location character varying(256),
        summary character varying(2048),
        language character varying(8) NOT NULL DEFAULT 'zh-TW',
        source character varying(64),
        source_url character varying(1024),
        original_file_name character varying(256),
        original_file_url character varying(1024),
        parse_status character varying(16) NOT NULL DEFAULT 'Pending',
        created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_investor_conference" PRIMARY KEY (id),
        CONSTRAINT "FK_investor_conference_document_document_id" FOREIGN KEY (document_id) REFERENCES document (id) ON DELETE SET NULL,
        CONSTRAINT "FK_investor_conference_security_security_id" FOREIGN KEY (security_id) REFERENCES security (id) ON DELETE CASCADE,
        CONSTRAINT "FK_investor_conference_uploaded_file_uploaded_file_id" FOREIGN KEY (uploaded_file_id) REFERENCES uploaded_file (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617094804_AddInvestorConference') THEN
    CREATE INDEX "IX_investor_conference_document_id" ON investor_conference (document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617094804_AddInvestorConference') THEN
    CREATE INDEX "IX_investor_conference_security_id_event_date" ON investor_conference (security_id, event_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617094804_AddInvestorConference') THEN
    CREATE INDEX "IX_investor_conference_uploaded_file_id" ON investor_conference (uploaded_file_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617094804_AddInvestorConference') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260617094804_AddInvestorConference', '10.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE financial_filing DROP CONSTRAINT "FK_financial_filing_app_user_uploaded_by_user_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE uploaded_file DROP CONSTRAINT "FK_uploaded_file_app_user_uploaded_by_user_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE uploaded_file ALTER COLUMN uploaded_by_user_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE financial_filing ALTER COLUMN uploaded_by_user_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE financial_filing ADD CONSTRAINT "FK_financial_filing_app_user_uploaded_by_user_id" FOREIGN KEY (uploaded_by_user_id) REFERENCES app_user (id) ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    ALTER TABLE uploaded_file ADD CONSTRAINT "FK_uploaded_file_app_user_uploaded_by_user_id" FOREIGN KEY (uploaded_by_user_id) REFERENCES app_user (id) ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260617100014_MakeUploadedByUserIdNullable') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260617100014_MakeUploadedByUserIdNullable', '10.0.8');
    END IF;
END $EF$;
COMMIT;

