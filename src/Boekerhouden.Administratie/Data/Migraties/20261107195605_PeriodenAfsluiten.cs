using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class PeriodenAfsluiten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AfgeslotenDoor",
                table: "Administraties",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AfgeslotenOp",
                table: "Administraties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AfgeslotenTot",
                table: "Administraties",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AfgeslotenDoor",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "AfgeslotenOp",
                table: "Administraties");

            migrationBuilder.DropColumn(
                name: "AfgeslotenTot",
                table: "Administraties");
        }
    }
}
