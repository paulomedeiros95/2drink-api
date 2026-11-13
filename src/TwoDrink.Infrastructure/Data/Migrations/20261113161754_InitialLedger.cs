using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TwoDrink.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    check_in_id = table.Column<Guid>(type: "uuid", nullable: true),
                    drink_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: true),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    points = table.Column<decimal>(type: "numeric(8,1)", precision: 8, scale: 1, nullable: false),
                    reverses_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    actor_admin = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prev_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id_sequence",
                table: "ledger_entries",
                columns: new[] { "account_id", "sequence" },
                unique: true);

            // Append-only enforcement lives in the database: the trigger stops everyone (owner included),
            // and the app role is not even granted UPDATE/DELETE/TRUNCATE on the ledger.
            migrationBuilder.Sql(@"
CREATE FUNCTION ledger_block_mutation() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    RAISE EXCEPTION 'ledger is append-only';
END;
$fn$;

CREATE TRIGGER ledger_entries_no_update_delete
    BEFORE UPDATE OR DELETE ON ledger_entries
    FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

CREATE TRIGGER ledger_entries_no_truncate
    BEFORE TRUNCATE ON ledger_entries
    FOR EACH STATEMENT EXECUTE FUNCTION ledger_block_mutation();
");

            // Roles are cluster-wide, so creation must be idempotent.
            migrationBuilder.Sql(@"
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'twodrink_app') THEN
        CREATE ROLE twodrink_app NOLOGIN;
    END IF;
END $$;

GRANT USAGE ON SCHEMA public TO twodrink_app;

-- Tables created by later migrations inherit full DML for the app role.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO twodrink_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO twodrink_app;

-- Ledger tables are created in this migration, so they get explicit, append-only grants.
GRANT SELECT, INSERT ON ledger_accounts TO twodrink_app;
GRANT SELECT, INSERT ON ledger_entries TO twodrink_app;
REVOKE UPDATE, DELETE, TRUNCATE ON ledger_entries FROM twodrink_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO twodrink_app;

GRANT SELECT ON ""__EFMigrationsHistory"" TO twodrink_app;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER ledger_entries_no_truncate ON ledger_entries;
DROP TRIGGER ledger_entries_no_update_delete ON ledger_entries;
DROP FUNCTION ledger_block_mutation();
");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "ledger_accounts");
        }
    }
}
