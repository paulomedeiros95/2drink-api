using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LedgerLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_check_in_id",
                table: "ledger_entries",
                column: "check_in_id",
                filter: "check_in_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_reverses_entry_id",
                table: "ledger_entries",
                column: "reverses_entry_id",
                filter: "reverses_entry_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_check_in_id",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_reverses_entry_id",
                table: "ledger_entries");
        }
    }
}
