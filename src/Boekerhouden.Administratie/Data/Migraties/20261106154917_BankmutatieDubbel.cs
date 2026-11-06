using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class BankmutatieDubbel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MogelijkDubbel",
                table: "Banktransacties",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MogelijkDubbel",
                table: "Banktransacties");
        }
    }
}
