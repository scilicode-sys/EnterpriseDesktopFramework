using System;
using Framework.Data.Postgres;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Framework.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comptes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NomComplet = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Actif = table.Column<bool>(type: "boolean", nullable: false),
                    EchecsConsecutifs = table.Column<int>(type: "integer", nullable: false),
                    GelJusqua = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comptes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comptes_Username",
                table: "Comptes",
                column: "Username",
                unique: true);

            // Synchro inter-postes : sans ce trigger, pg_notify n'est jamais émis
            // et les postes se croient éternlement seuls. Table par table, à
            // reproduire dans chaque migration ajoutant une table synchronisée.
            migrationBuilder.Sql(SyncNotifySql.Script("framework_sync", new[] { "Comptes" }));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TX_sync_Comptes" ON "Comptes";
                DROP FUNCTION IF EXISTS framework_notify_sync();
                """);

            migrationBuilder.DropTable(
                name: "Comptes");
        }
    }
}
