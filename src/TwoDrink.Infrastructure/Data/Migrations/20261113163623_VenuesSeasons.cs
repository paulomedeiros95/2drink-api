using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VenuesSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "venues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    cached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "venue_sponsorships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    badge_art_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venue_sponsorships", x => x.id);
                    table.ForeignKey(
                        name: "fk_venue_sponsorships_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "seasons",
                columns: new[] { "id", "end_date", "name", "start_date" },
                values: new object[,]
                {
                    { new Guid("5ea50000-2026-4000-8000-000000000002"), new DateOnly(2026, 12, 31), "2026 – 2º semestre", new DateOnly(2026, 7, 1) },
                    { new Guid("5ea50000-2027-4000-8000-000000000001"), new DateOnly(2027, 6, 30), "2027 – 1º semestre", new DateOnly(2027, 1, 1) }
                });

            // Seasons never overlap, even under concurrent inserts (SeasonService maps 23P01 to season_overlap).
            migrationBuilder.Sql(
                "ALTER TABLE seasons ADD CONSTRAINT ex_seasons_no_overlap " +
                "EXCLUDE USING gist (daterange(start_date, end_date, '[]') WITH &&);");

            migrationBuilder.CreateIndex(
                name: "ix_seasons_start_date_end_date",
                table: "seasons",
                columns: new[] { "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_venue_sponsorships_venue_id_starts_at",
                table: "venue_sponsorships",
                columns: new[] { "venue_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_venues_place_id",
                table: "venues",
                column: "place_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "venue_sponsorships");

            migrationBuilder.DropTable(
                name: "venues");
        }
    }
}
