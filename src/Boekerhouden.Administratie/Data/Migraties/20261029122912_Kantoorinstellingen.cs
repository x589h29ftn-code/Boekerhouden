using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Kantoorinstellingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "DrempelAutomatisch",
                table: "Administraties",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Betalingstermijn",
                table: "Administraties",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "Adrescontrole",
                table: "Administraties",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Bandbreedte",
                table: "Administraties",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Documentmap",
                table: "Administraties",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LimietAfwijkend",
                table: "Administraties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Kantoorinstellingen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    DrempelAutomatisch = table.Column<int>(type: "integer", nullable: false),
                    Bandbreedte = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    LimietAutomatisch = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Betalingstermijn = table.Column<int>(type: "integer", nullable: false),
                    Adrescontrole = table.Column<bool>(type: "boolean", nullable: false),
                    Documentmap = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kantoorinstellingen", x => x.Id);
                });

            // Wat gelijk was aan de oude standaard volgt voortaan het kantoor; een ingestelde limiet blijft eigen.
            migrationBuilder.Sql("""
                UPDATE "Administraties" SET "Betalingstermijn" = NULL WHERE "Betalingstermijn" = 30;
                UPDATE "Administraties" SET "DrempelAutomatisch" = NULL WHERE "DrempelAutomatisch" = 2;
                UPDATE "Administraties" SET "LimietAfwijkend" = TRUE WHERE "LimietAutomatisch" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kantoorinstellingen");

            migrationBuilder.DropColumn(
                name: "Adrescontrole",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Bandbreedte",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "Documentmap",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "LimietAfwijkend",
                table: "Administraties");

            migrationBuilder.AlterColumn<int>(
                name: "DrempelAutomatisch",
                table: "Administraties",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Betalingstermijn",
                table: "Administraties",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
