using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class SamenEnSortering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SamenMetId",
                table: "RekeningPresentaties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sortering",
                table: "PostPresentaties",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RekeningPresentaties_SamenMetId",
                table: "RekeningPresentaties",
                column: "SamenMetId");

            migrationBuilder.AddForeignKey(
                name: "FK_RekeningPresentaties_Grootboekrekeningen_SamenMetId",
                table: "RekeningPresentaties",
                column: "SamenMetId",
                principalTable: "Grootboekrekeningen",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RekeningPresentaties_Grootboekrekeningen_SamenMetId",
                table: "RekeningPresentaties");

            migrationBuilder.DropIndex(
                name: "IX_RekeningPresentaties_SamenMetId",
                table: "RekeningPresentaties");

            migrationBuilder.DropColumn(
                name: "SamenMetId",
                table: "RekeningPresentaties");

            migrationBuilder.DropColumn(
                name: "Sortering",
                table: "PostPresentaties");
        }
    }
}
