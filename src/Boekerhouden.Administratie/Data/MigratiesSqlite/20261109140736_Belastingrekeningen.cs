using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Belastingrekeningen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BelastingAutomatisch",
                table: "Administraties",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RekeningInkomstenbelasting",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RekeningLoonheffingen",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RekeningOmzetbelasting",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RekeningVennootschapsbelasting",
                table: "Administraties",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BelastingAutomatisch",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "RekeningInkomstenbelasting",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "RekeningLoonheffingen",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "RekeningOmzetbelasting",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "RekeningVennootschapsbelasting",
                table: "Administraties");
        }
    }
}
