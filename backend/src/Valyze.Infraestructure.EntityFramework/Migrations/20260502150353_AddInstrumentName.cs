using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valyze.Infraestructure.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddInstrumentName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "instrument_name",
                table: "trades",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "instrument_name",
                table: "trades");
        }
    }
}
