using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class BelastingMrbEnAutomatisch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RekeningMotorrijtuigenbelasting",
                table: "Administraties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Betalingen aan de Belastingdienst met een geldig kenmerk zelf boeken: voortaan standaard aan.
            migrationBuilder.Sql("UPDATE \"Administraties\" SET \"BelastingAutomatisch\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RekeningMotorrijtuigenbelasting",
                table: "Administraties");
        }
    }
}
