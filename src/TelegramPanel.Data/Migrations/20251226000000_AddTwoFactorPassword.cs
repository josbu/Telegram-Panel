using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20251226000000_AddTwoFactorPassword")]
    /// <inheritdoc />
    public partial class AddTwoFactorPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwoFactorPassword",
                table: "Accounts",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorPassword",
                table: "Accounts");
        }
    }
}
