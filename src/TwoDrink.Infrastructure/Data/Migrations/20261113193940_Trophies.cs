using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Trophies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trophies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trophies", x => x.id);
                    table.ForeignKey(
                        name: "fk_trophies_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trophies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id_created_at",
                table: "ledger_entries",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trophies_league_id_user_id",
                table: "trophies",
                columns: new[] { "league_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trophies_user_id",
                table: "trophies",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trophies");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_account_id_created_at",
                table: "ledger_entries");
        }
    }
}
