using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valyze.Infraestructure.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class RelaxInstrumentRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "isin",
                table: "trades",
                newName: "instrument");

            migrationBuilder.AlterColumn<string>(
                name: "instrument",
                table: "trades",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "instrument",
                table: "trades",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.RenameColumn(
                name: "instrument",
                table: "trades",
                newName: "isin");
        }
    }
}
