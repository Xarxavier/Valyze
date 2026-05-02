using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Valyze.Infraestructure.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class NewsIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "news_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    url_template = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    polling_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "news_articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_articles", x => x.id);
                    table.ForeignKey(
                        name: "FK_news_articles_news_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "news_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "news_article_instruments",
                columns: table => new
                {
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_article_instruments", x => new { x.article_id, x.instrument });
                    table.ForeignKey(
                        name: "FK_news_article_instruments_news_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "news_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_news_article_instruments_instrument",
                table: "news_article_instruments",
                column: "instrument");

            migrationBuilder.CreateIndex(
                name: "ix_news_articles_published_at",
                table: "news_articles",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "ix_news_articles_source_id",
                table: "news_articles",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_news_articles_url_unique",
                table: "news_articles",
                column: "url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_news_sources_enabled",
                table: "news_sources",
                column: "enabled");

            migrationBuilder.CreateIndex(
                name: "ix_news_sources_url_unique",
                table: "news_sources",
                column: "url_template",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "news_article_instruments");

            migrationBuilder.DropTable(
                name: "news_articles");

            migrationBuilder.DropTable(
                name: "news_sources");
        }
    }
}
