using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
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
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            // Betalingen aan de Belastingdienst met een geldig kenmerk zelf boeken: voortaan standaard aan.
            migrationBuilder.Sql("UPDATE \"Administraties\" SET \"BelastingAutomatisch\" = 1;");
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
