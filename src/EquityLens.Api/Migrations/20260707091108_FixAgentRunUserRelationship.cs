using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixAgentRunUserRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_agent_run_app_user_user_id",
                table: "agent_run",
                column: "user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_run_app_user_user_id",
                table: "agent_run");
        }
    }
}
