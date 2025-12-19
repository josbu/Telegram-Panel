using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20251219000004_AccountUserIdIndexNotUnique")]
    public partial class AccountUserIdIndexNotUnique : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 兼容：某些历史库可能没有该索引（例如曾用 EnsureCreated 创建过不同 schema）
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Accounts_UserId;");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Accounts_UserId;");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserId",
                table: "Accounts",
                column: "UserId",
                unique: true);
        }
    }
}
