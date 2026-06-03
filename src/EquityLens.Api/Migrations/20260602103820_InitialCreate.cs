using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "User"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk_model_setting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lookback_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 252),
                    confidence_level = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValue: 0.95m),
                    holding_period_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Historical"),
                    return_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Log"),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_model_setting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scenario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    shock_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    shock_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ticker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    exchange = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    asset_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    sector = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    industry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_app_user_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "uploaded_file",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    storage_provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "MinIO"),
                    upload_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uploaded_file", x => x.id);
                    table.ForeignKey(
                        name: "FK_uploaded_file_app_user_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_statement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    fiscal_quarter = table.Column<int>(type: "integer", nullable: true),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    published_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    data_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_statement", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_statement_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_price",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    interval = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "1d"),
                    open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    adjusted_close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    volume = table.Column<long>(type: "bigint", nullable: true),
                    data_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_price", x => x.id);
                    table.ForeignKey(
                        name: "FK_market_price_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_report",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Draft"),
                    summary = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_report", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_report_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_holding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    average_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_holding", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_holding_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_portfolio_holding_security_security_id",
                        column: x => x.security_id,
                        principalTable: "security",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_snapshot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    market_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    daily_return = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_snapshot", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_snapshot_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "risk_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_model_setting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_run_portfolio_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_risk_run_risk_model_setting_risk_model_setting_id",
                        column: x => x.risk_model_setting_id,
                        principalTable: "risk_model_setting",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    uploaded_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "en"),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    parsed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    parse_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_uploaded_file_uploaded_file_id",
                        column: x => x.uploaded_file_id,
                        principalTable: "uploaded_file",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_line_item",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    financial_statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_line_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_financial_line_item_financial_statement_financial_statement~",
                        column: x => x.financial_statement_id,
                        principalTable: "financial_statement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_memo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    risk_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    model_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_memo", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_memo_financial_report_financial_report_id",
                        column: x => x.financial_report_id,
                        principalTable: "financial_report",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ai_memo_risk_run_risk_run_id",
                        column: x => x.risk_run_id,
                        principalTable: "risk_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_run",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    risk_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Queued"),
                    progress_percent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    redis_job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_run", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_run_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_job_run_financial_report_financial_report_id",
                        column: x => x.financial_report_id,
                        principalTable: "financial_report",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_job_run_risk_run_risk_run_id",
                        column: x => x.risk_run_id,
                        principalTable: "risk_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_job_run_uploaded_file_uploaded_file_id",
                        column: x => x.uploaded_file_id,
                        principalTable: "uploaded_file",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "risk_metric",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    risk_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    metric_value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    confidence_level = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    holding_period_days = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_metric", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_metric_risk_run_risk_run_id",
                        column: x => x.risk_run_id,
                        principalTable: "risk_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_result",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    risk_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_value_before = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    portfolio_value_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pnl_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pnl_percent = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_result", x => x.id);
                    table.ForeignKey(
                        name: "FK_scenario_result_risk_run_risk_run_id",
                        column: x => x.risk_run_id,
                        principalTable: "risk_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_result_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_chunk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    section_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunk", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_chunk_document_document_id",
                        column: x => x.document_id,
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "critic_note",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_memo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reviewer_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Info"),
                    comment = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_critic_note", x => x.id);
                    table.ForeignKey(
                        name: "FK_critic_note_ai_memo_ai_memo_id",
                        column: x => x.ai_memo_id,
                        principalTable: "ai_memo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "citation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_memo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_chunk_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    source_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    reference_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    quote_text = table.Column<string>(type: "text", nullable: true),
                    relevance_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_citation", x => x.id);
                    table.ForeignKey(
                        name: "FK_citation_ai_memo_ai_memo_id",
                        column: x => x.ai_memo_id,
                        principalTable: "ai_memo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_citation_document_chunk_document_chunk_id",
                        column: x => x.document_chunk_id,
                        principalTable: "document_chunk",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "document_embedding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_embedding", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_embedding_document_chunk_document_chunk_id",
                        column: x => x.document_chunk_id,
                        principalTable: "document_chunk",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_memo_financial_report_id",
                table: "ai_memo",
                column: "financial_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_memo_risk_run_id",
                table: "ai_memo",
                column: "risk_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_app_user_email",
                table: "app_user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_citation_ai_memo_id",
                table: "citation",
                column: "ai_memo_id");

            migrationBuilder.CreateIndex(
                name: "IX_citation_document_chunk_id",
                table: "citation",
                column: "document_chunk_id");

            migrationBuilder.CreateIndex(
                name: "IX_critic_note_ai_memo_id",
                table: "critic_note",
                column: "ai_memo_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_uploaded_file_id",
                table: "document",
                column: "uploaded_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunk_document_id_chunk_index",
                table: "document_chunk",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_embedding_document_chunk_id",
                table: "document_embedding",
                column: "document_chunk_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_line_item_financial_statement_id_code",
                table: "financial_line_item",
                columns: new[] { "financial_statement_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_report_portfolio_id",
                table: "financial_report",
                column: "portfolio_id");

            migrationBuilder.CreateIndex(
                name: "IX_financial_statement_security_id_statement_type_period_type_~",
                table: "financial_statement",
                columns: new[] { "security_id", "statement_type", "period_type", "fiscal_year", "fiscal_quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_run_created_by_user_id",
                table: "job_run",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_run_financial_report_id",
                table: "job_run",
                column: "financial_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_run_risk_run_id",
                table: "job_run",
                column: "risk_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_run_uploaded_file_id",
                table: "job_run",
                column: "uploaded_file_id");

            migrationBuilder.CreateIndex(
                name: "IX_market_price_security_id_interval_price_time",
                table: "market_price",
                columns: new[] { "security_id", "interval", "price_time" });

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_owner_user_id",
                table: "portfolio",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_holding_portfolio_id",
                table: "portfolio_holding",
                column: "portfolio_id");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_holding_security_id",
                table: "portfolio_holding",
                column: "security_id");

            migrationBuilder.CreateIndex(
                name: "IX_portfolio_snapshot_portfolio_id_snapshot_date",
                table: "portfolio_snapshot",
                columns: new[] { "portfolio_id", "snapshot_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_risk_metric_risk_run_id",
                table: "risk_metric",
                column: "risk_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_run_portfolio_id",
                table: "risk_run",
                column: "portfolio_id");

            migrationBuilder.CreateIndex(
                name: "IX_risk_run_risk_model_setting_id",
                table: "risk_run",
                column: "risk_model_setting_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_risk_run_id",
                table: "scenario_result",
                column: "risk_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_scenario_id",
                table: "scenario_result",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_security_ticker_exchange",
                table: "security",
                columns: new[] { "ticker", "exchange" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_file_object_key",
                table: "uploaded_file",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_file_uploaded_by_user_id",
                table: "uploaded_file",
                column: "uploaded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "citation");

            migrationBuilder.DropTable(
                name: "critic_note");

            migrationBuilder.DropTable(
                name: "document_embedding");

            migrationBuilder.DropTable(
                name: "financial_line_item");

            migrationBuilder.DropTable(
                name: "job_run");

            migrationBuilder.DropTable(
                name: "market_price");

            migrationBuilder.DropTable(
                name: "portfolio_holding");

            migrationBuilder.DropTable(
                name: "portfolio_snapshot");

            migrationBuilder.DropTable(
                name: "risk_metric");

            migrationBuilder.DropTable(
                name: "scenario_result");

            migrationBuilder.DropTable(
                name: "ai_memo");

            migrationBuilder.DropTable(
                name: "document_chunk");

            migrationBuilder.DropTable(
                name: "financial_statement");

            migrationBuilder.DropTable(
                name: "scenario");

            migrationBuilder.DropTable(
                name: "financial_report");

            migrationBuilder.DropTable(
                name: "risk_run");

            migrationBuilder.DropTable(
                name: "document");

            migrationBuilder.DropTable(
                name: "security");

            migrationBuilder.DropTable(
                name: "portfolio");

            migrationBuilder.DropTable(
                name: "risk_model_setting");

            migrationBuilder.DropTable(
                name: "uploaded_file");

            migrationBuilder.DropTable(
                name: "app_user");
        }
    }
}
