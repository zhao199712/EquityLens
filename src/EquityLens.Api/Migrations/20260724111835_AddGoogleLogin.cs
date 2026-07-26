using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "google_subject",
                table: "app_user",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_google_subject",
                table: "app_user",
                column: "google_subject",
                unique: true,
                filter: "google_subject IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_app_user_google_subject",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "google_subject",
                table: "app_user");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
