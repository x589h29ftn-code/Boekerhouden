using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
{
    /// <inheritdoc />
    public partial class Correcties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ControleAfwijking",
                table: "Relaties",
                type: "INTEGER",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ControleVanaf",
                table: "Relaties",
                type: "INTEGER",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BtwCode",
                table: "Rekeningen",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Herstelt",
                table: "Boekingen",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Vervangt",
                table: "Boekingen",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_Herstelt",
                table: "Boekingen",
                column: "Herstelt");

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_Vervangt",
                table: "Boekingen",
                column: "Vervangt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Boekingen_Herstelt",
                table: "Boekingen");

            migrationBuilder.DropIndex(
                name: "IX_Boekingen_Vervangt",
                table: "Boekingen");

            migrationBuilder.DropColumn(
                name: "ControleAfwijking",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "ControleVanaf",
                table: "Relaties");

            migrationBuilder.DropColumn(
                name: "BtwCode",
                table: "Rekeningen");

            migrationBuilder.DropColumn(
                name: "Herstelt",
                table: "Boekingen");

            migrationBuilder.DropColumn(
                name: "Vervangt",
                table: "Boekingen");
        }
    }
}
