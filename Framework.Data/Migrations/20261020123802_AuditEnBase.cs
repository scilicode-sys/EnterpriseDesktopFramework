using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Framework.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditEnBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Exemples",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Exemples",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Exemples",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Comptes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Comptes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Comptes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Exemples");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Exemples");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Exemples");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Comptes");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Comptes");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Comptes");
        }
    }
}
