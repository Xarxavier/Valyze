using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valyze.Infraestructure.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time cleanup: rows imported before broker reference tracking
            // had no source identifier and would duplicate on re-import. Wipe
            // them so dedup starts from a clean slate.
            migrationBuilder.Sql("DELETE FROM trades;");

            migrationBuilder.AddColumn<string>(
                name: "broker_key",
                table: "trades",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "broker_reference",
                table: "trades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trades_broker_reference_unique",
                table: "trades",
                columns: new[] { "account_id", "broker_key", "broker_reference" },
                unique: true,
                filter: "\"broker_reference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trades_broker_reference_unique",
                table: "trades");

            migrationBuilder.DropColumn(
                name: "broker_key",
                table: "trades");

            migrationBuilder.DropColumn(
                name: "broker_reference",
                table: "trades");
        }
    }
}
