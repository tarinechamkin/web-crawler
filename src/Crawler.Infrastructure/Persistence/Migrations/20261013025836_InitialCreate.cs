using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crawler.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crawl_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    max_depth = table.Column<int>(type: "integer", nullable: false),
                    max_pages = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    pages_crawled = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crawled_pages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    parent_page_id = table.Column<long>(type: "bigint", nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    outgoing_link_count = table.Column<int>(type: "integer", nullable: false),
                    internal_link_count = table.Column<int>(type: "integer", nullable: false),
                    domain_link_ratio = table.Column<double>(type: "double precision", nullable: false),
                    crawled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawled_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_crawled_pages_crawl_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "crawl_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_crawled_pages_crawled_pages_parent_page_id",
                        column: x => x.parent_page_id,
                        principalTable: "crawled_pages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "page_links",
                columns: table => new
                {
                    page_id = table.Column<long>(type: "bigint", nullable: false),
                    target_url = table.Column<string>(type: "text", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_links", x => new { x.page_id, x.target_url });
                    table.ForeignKey(
                        name: "fk_page_links_crawl_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "crawl_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_page_links_crawled_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "crawled_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crawl_jobs_created_at_id",
                table: "crawl_jobs",
                columns: new[] { "created_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_crawled_pages_job_id_url",
                table: "crawled_pages",
                columns: new[] { "job_id", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crawled_pages_parent_page_id",
                table: "crawled_pages",
                column: "parent_page_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_links_job_id",
                table: "page_links",
                column: "job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_links");

            migrationBuilder.DropTable(
                name: "crawled_pages");

            migrationBuilder.DropTable(
                name: "crawl_jobs");
        }
    }
}
