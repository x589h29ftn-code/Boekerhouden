using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Verwerkingswijze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Verwerking",
                table: "Verkoopdocumenten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Onbekend");

            migrationBuilder.AddColumn<string>(
                name: "Verwerking",
                table: "Documenten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Onbekend");

            migrationBuilder.AddColumn<string>(
                name: "Verwerking",
                table: "Banktransacties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Onbekend");
            // Wat al geboekt is, zo goed als het nog te zien is: een inkoopfactuur met "Automatisch geboekt." is
            // autocodering; een bankmutatie zonder melding is door de gebruiker geboekt, met melding vanzelf (afgeletterd
            // als de boeking een factuurnummer raakt); een zelf gemaakte verkoopfactuur is handmatig, een ingelezen
            // zonder melding vanzelf geboekt.
            migrationBuilder.Sql("""
                UPDATE "Documenten" SET "Verwerking" = CASE WHEN "Melding" = 'Automatisch geboekt.' THEN 'Autocodering' ELSE 'Handmatig' END
                WHERE "Status" = 'Geboekt';
                """);
            migrationBuilder.Sql("""
                UPDATE "Banktransacties" SET "Verwerking" = CASE
                    WHEN "Melding" IS NULL THEN 'Handmatig'
                    WHEN EXISTS (SELECT 1 FROM "Boekingsregels" r WHERE r."BoekingId" = "Banktransacties"."BoekingId" AND r."Factuurnummer" IS NOT NULL) THEN 'Afgeletterd'
                    ELSE 'Autocodering' END
                WHERE "Status" = 'Geboekt';
                """);
            migrationBuilder.Sql("""
                UPDATE "Verkoopdocumenten" SET "Verwerking" = CASE WHEN "Ingelezen" = FALSE OR "Melding" IS NOT NULL THEN 'Handmatig' ELSE 'Automatisch' END
                WHERE "Status" = 'Definitief';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Verwerking",
                table: "Verkoopdocumenten");

            migrationBuilder.DropColumn(
                name: "Verwerking",
                table: "Documenten");

            migrationBuilder.DropColumn(
                name: "Verwerking",
                table: "Banktransacties");
        }
    }
}
