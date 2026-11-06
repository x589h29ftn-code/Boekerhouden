using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class ArchiverenEnVerwijderen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GearchiveerdDoor",
                table: "Administraties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GearchiveerdOp",
                table: "Administraties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerwijderdeAdministraties",
                columns: table => new
                {
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VerwijderdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerwijderdDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerwijderdeAdministraties", x => x.AdministratieId);
                });

            // Een verwijderde administratie (rij in VerwijderdeAdministraties) mag zijn boekingen kwijt; verder blijven
            // boekingen onwijzigbaar. Het wijzigingslog blijft altijd onwijzigbaar.
            migrationBuilder.Sql("""
                CREATE FUNCTION weiger_wijziging_tenzij_verwijderd() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND EXISTS (SELECT 1 FROM "VerwijderdeAdministraties" v WHERE v."AdministratieId" = OLD."AdministratieId") THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'Gegevens in % kunnen niet worden gewijzigd of verwijderd.', TG_TABLE_NAME
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$;
                DROP TRIGGER boekingen_onwijzigbaar ON "Boekingen";
                DROP TRIGGER boekingsregels_onwijzigbaar ON "Boekingsregels";
                CREATE TRIGGER boekingen_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingen"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging_tenzij_verwijderd();
                CREATE TRIGGER boekingsregels_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingsregels"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging_tenzij_verwijderd();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER boekingen_onwijzigbaar ON "Boekingen";
                DROP TRIGGER boekingsregels_onwijzigbaar ON "Boekingsregels";
                CREATE TRIGGER boekingen_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingen"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging();
                CREATE TRIGGER boekingsregels_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingsregels"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging();
                DROP FUNCTION weiger_wijziging_tenzij_verwijderd();
                """);

            migrationBuilder.DropTable(
                name: "VerwijderdeAdministraties");

            migrationBuilder.DropColumn(
                name: "GearchiveerdDoor",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "GearchiveerdOp",
                table: "Administraties");
        }
    }
}
