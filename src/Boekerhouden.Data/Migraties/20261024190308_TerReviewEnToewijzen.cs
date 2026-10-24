using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class TerReviewEnToewijzen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ToegewezenAan",
                table: "Reviewpunten",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ToegewezenAanId",
                table: "Reviewpunten",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerReviewDoor",
                table: "DossierOnderdelen",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TerReviewOp",
                table: "DossierOnderdelen",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviewpunten_ToegewezenAanId",
                table: "Reviewpunten",
                column: "ToegewezenAanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviewpunten_ToegewezenAanId",
                table: "Reviewpunten");

            migrationBuilder.DropColumn(
                name: "ToegewezenAan",
                table: "Reviewpunten");

            migrationBuilder.DropColumn(
                name: "ToegewezenAanId",
                table: "Reviewpunten");

            migrationBuilder.DropColumn(
                name: "TerReviewDoor",
                table: "DossierOnderdelen");

            migrationBuilder.DropColumn(
                name: "TerReviewOp",
                table: "DossierOnderdelen");
        }
    }
}
