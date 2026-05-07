using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valyze.Infraestructure.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentDecisionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investment_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ticker = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    quantity_amount = table.Column<decimal>(type: "numeric(28,8)", nullable: true),
                    quantity_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    quantity_units = table.Column<short>(type: "smallint", nullable: false),
                    price_at_decision_amount = table.Column<decimal>(type: "numeric(28,8)", nullable: true),
                    price_at_decision_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    evaluation_horizon_days = table.Column<int>(type: "integer", nullable: false),
                    ai_chat_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_trade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_other_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_decisions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_investment_decisions_trades_linked_trade_id",
                        column: x => x.linked_trade_id,
                        principalTable: "trades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_investment_decisions_account_created_at",
                table: "investment_decisions",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_decisions_account_isin",
                table: "investment_decisions",
                columns: new[] { "account_id", "isin" },
                filter: "\"isin\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_investment_decisions_account_source_action_created_at",
                table: "investment_decisions",
                columns: new[] { "account_id", "source", "action", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_decisions_linked_trade_id",
                table: "investment_decisions",
                column: "linked_trade_id",
                filter: "\"linked_trade_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_decisions");
        }
    }
}
