using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TelegramPanel.Data;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260809000100_AddWireGuardWarpProxyKind")]
    public partial class AddWireGuardWarpProxyKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RebuildOutboundProxies(migrationBuilder, includeWireGuardWarp: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"OutboundProxies\" SET \"Kind\" = 'manual' WHERE \"Kind\" = 'wireguard_warp';",
                suppressTransaction: true);
            RebuildOutboundProxies(migrationBuilder, includeWireGuardWarp: false);
        }

        private static void RebuildOutboundProxies(
            MigrationBuilder migrationBuilder,
            bool includeWireGuardWarp)
        {
            var kindConstraint = includeWireGuardWarp
                ? "\"Kind\" IN ('manual', 'resin', 'warp', 'wireguard_warp')"
                : "\"Kind\" IN ('manual', 'resin', 'warp')";

            migrationBuilder.Sql(
                $$"""
                PRAGMA foreign_keys=OFF;

                CREATE TABLE "__ef_temp_OutboundProxies" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_OutboundProxies" PRIMARY KEY AUTOINCREMENT,
                    "CategoryId" INTEGER NULL,
                    "Name" TEXT NOT NULL,
                    "Kind" TEXT NOT NULL DEFAULT 'manual',
                    "Protocol" TEXT NOT NULL,
                    "Host" TEXT NOT NULL,
                    "Port" INTEGER NOT NULL,
                    "Username" TEXT NULL,
                    "Password" TEXT NULL,
                    "Secret" TEXT NULL,
                    "ResinPlatform" TEXT NULL,
                    "ResinAdminUrl" TEXT NULL,
                    "ResinAdminToken" TEXT NULL,
                    "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                    "TestStatus" TEXT NOT NULL DEFAULT 'unknown',
                    "LastError" TEXT NULL,
                    "LastLatencyMs" INTEGER NULL,
                    "EgressIp" TEXT NULL,
                    "EgressCountry" TEXT NULL,
                    "EgressCity" TEXT NULL,
                    "EgressIsp" TEXT NULL,
                    "LastTestedAtUtc" TEXT NULL,
                    "FirstBoundAtUtc" TEXT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "UpdatedAtUtc" TEXT NOT NULL,
                    CONSTRAINT "CK_OutboundProxies_Kind" CHECK ({{kindConstraint}}),
                    CONSTRAINT "CK_OutboundProxies_LastLatencyMs" CHECK ("LastLatencyMs" IS NULL OR "LastLatencyMs" >= 0),
                    CONSTRAINT "CK_OutboundProxies_Port" CHECK ("Port" BETWEEN 1 AND 65535),
                    CONSTRAINT "CK_OutboundProxies_Protocol" CHECK ("Protocol" IN ('http', 'socks5', 'mtproto')),
                    CONSTRAINT "CK_OutboundProxies_TestStatus" CHECK ("TestStatus" IN ('unknown', 'ok', 'fail')),
                    CONSTRAINT "FK_OutboundProxies_ProxyCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "ProxyCategories" ("Id") ON DELETE SET NULL
                );

                INSERT INTO "__ef_temp_OutboundProxies" (
                    "Id", "CategoryId", "Name", "Kind", "Protocol", "Host", "Port",
                    "Username", "Password", "Secret", "ResinPlatform", "ResinAdminUrl", "ResinAdminToken",
                    "IsEnabled", "TestStatus", "LastError", "LastLatencyMs", "EgressIp", "EgressCountry",
                    "EgressCity", "EgressIsp", "LastTestedAtUtc", "FirstBoundAtUtc", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    "Id", "CategoryId", "Name", "Kind", "Protocol", "Host", "Port",
                    "Username", "Password", "Secret", "ResinPlatform", "ResinAdminUrl", "ResinAdminToken",
                    "IsEnabled", "TestStatus", "LastError", "LastLatencyMs", "EgressIp", "EgressCountry",
                    "EgressCity", "EgressIsp", "LastTestedAtUtc", "FirstBoundAtUtc", "CreatedAtUtc", "UpdatedAtUtc"
                FROM "OutboundProxies";

                DROP TABLE "OutboundProxies";
                ALTER TABLE "__ef_temp_OutboundProxies" RENAME TO "OutboundProxies";

                CREATE INDEX "IX_OutboundProxies_CategoryId" ON "OutboundProxies" ("CategoryId");
                CREATE INDEX "IX_OutboundProxies_EgressIp" ON "OutboundProxies" ("EgressIp");
                CREATE INDEX "IX_OutboundProxies_IsEnabled_Kind" ON "OutboundProxies" ("IsEnabled", "Kind");
                CREATE INDEX "IX_OutboundProxies_Name" ON "OutboundProxies" ("Name");
                CREATE INDEX "IX_OutboundProxies_Protocol" ON "OutboundProxies" ("Protocol");
                CREATE INDEX "IX_OutboundProxies_TestStatus" ON "OutboundProxies" ("TestStatus");

                PRAGMA foreign_keys=ON;
                """,
                suppressTransaction: true);
        }
    }
}
