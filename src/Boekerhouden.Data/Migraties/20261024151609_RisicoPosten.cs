using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class RisicoPosten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OvergenomenVan",
                table: "DossierStappen",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Beoordeling",
                table: "DossierPostKeuzes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NietVanToepassing");

            migrationBuilder.AddColumn<string>(
                name: "Risico",
                table: "DossierPostKeuzes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // De keuze materieel ja/nee wordt de beoordeling Materieel of NietVanToepassing.
            migrationBuilder.Sql("UPDATE \"DossierPostKeuzes\" SET \"Beoordeling\" = CASE WHEN \"Materieel\" THEN 'Materieel' ELSE 'NietVanToepassing' END;");

            migrationBuilder.DropColumn(
                name: "Materieel",
                table: "DossierPostKeuzes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvergenomenVan",
                table: "DossierStappen");

            migrationBuilder.DropColumn(
                name: "Beoordeling",
                table: "DossierPostKeuzes");

            migrationBuilder.DropColumn(
                name: "Risico",
                table: "DossierPostKeuzes");

            migrationBuilder.AddColumn<bool>(
                name: "Materieel",
                table: "DossierPostKeuzes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
