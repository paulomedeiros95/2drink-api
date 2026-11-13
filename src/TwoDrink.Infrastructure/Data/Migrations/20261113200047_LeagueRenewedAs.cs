using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LeagueRenewedAs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "renewed_as_league_id",
                table: "leagues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_leagues_renewed_as_league_id",
                table: "leagues",
                column: "renewed_as_league_id");

            migrationBuilder.AddForeignKey(
                name: "fk_leagues_leagues_renewed_as_league_id",
                table: "leagues",
                column: "renewed_as_league_id",
                principalTable: "leagues",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leagues_leagues_renewed_as_league_id",
                table: "leagues");

            migrationBuilder.DropIndex(
                name: "ix_leagues_renewed_as_league_id",
                table: "leagues");

            migrationBuilder.DropColumn(
                name: "renewed_as_league_id",
                table: "leagues");
        }
    }
}
