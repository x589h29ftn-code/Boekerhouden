using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Dashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dashboardindelingen",
                columns: table => new
                {
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Widgets = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GewijzigdOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dashboardindelingen", x => new { x.AdministratieId, x.GebruikerId });
                    table.ForeignKey(
                        name: "FK_Dashboardindelingen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dashboardindelingen");
        }
    }
}
