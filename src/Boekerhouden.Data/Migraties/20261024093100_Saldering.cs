using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Saldering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OmslagCode",
                table: "EigenPosten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OmslagCode",
                table: "EigenPosten");
        }
    }
}
