using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDisplayNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayNumber",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "Accounts"
                SET "DisplayNumber" = (
                    SELECT COUNT(*)
                    FROM "Accounts" AS "Earlier"
                    WHERE "Earlier"."Id" <= "Accounts"."Id"
                )
                WHERE "DisplayNumber" <= 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_DisplayNumber",
                table: "Accounts",
                column: "DisplayNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_DisplayNumber",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DisplayNumber",
                table: "Accounts");
        }
    }
}
