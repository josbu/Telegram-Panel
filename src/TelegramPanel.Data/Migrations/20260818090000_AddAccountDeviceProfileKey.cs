using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace TelegramPanel.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260818090000_AddAccountDeviceProfileKey")]
    public partial class AddAccountDeviceProfileKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceProfileKey",
                table: "Accounts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceProfileKey",
                table: "Accounts");
        }
    }
}
