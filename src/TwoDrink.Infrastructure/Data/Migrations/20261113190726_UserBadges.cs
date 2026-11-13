using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    art_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    check_in_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badges", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_badges_check_ins_check_in_id",
                        column: x => x.check_in_id,
                        principalTable: "check_ins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badges_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badges_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_check_in_id",
                table: "user_badges",
                column: "check_in_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_season_id",
                table: "user_badges",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_user_id_venue_id_season_id_tier",
                table: "user_badges",
                columns: new[] { "user_id", "venue_id", "season_id", "tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_venue_id",
                table: "user_badges",
                column: "venue_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_badges");
        }
    }
}
