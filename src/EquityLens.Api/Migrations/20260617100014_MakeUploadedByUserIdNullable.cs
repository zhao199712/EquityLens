using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquityLens.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeUploadedByUserIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_filing_app_user_uploaded_by_user_id",
                table: "financial_filing");

            migrationBuilder.DropForeignKey(
                name: "FK_uploaded_file_app_user_uploaded_by_user_id",
                table: "uploaded_file");

            migrationBuilder.AlterColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "uploaded_file",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "financial_filing",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_filing_app_user_uploaded_by_user_id",
                table: "financial_filing",
                column: "uploaded_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_uploaded_file_app_user_uploaded_by_user_id",
                table: "uploaded_file",
                column: "uploaded_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_filing_app_user_uploaded_by_user_id",
                table: "financial_filing");

            migrationBuilder.DropForeignKey(
                name: "FK_uploaded_file_app_user_uploaded_by_user_id",
                table: "uploaded_file");

            migrationBuilder.AlterColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "uploaded_file",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "uploaded_by_user_id",
                table: "financial_filing",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_filing_app_user_uploaded_by_user_id",
                table: "financial_filing",
                column: "uploaded_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_uploaded_file_app_user_uploaded_by_user_id",
                table: "uploaded_file",
                column: "uploaded_by_user_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
