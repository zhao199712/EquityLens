using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRiskProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_risk_profile",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    capacity_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    willingness_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    capacity_score = table.Column<int>(type: "integer", nullable: true),
                    willingness_score = table.Column<int>(type: "integer", nullable: true),
                    questionnaire_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    threshold_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: true),
                    assessed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_risk_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_risk_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_risk_profile");
        }
    }
}
