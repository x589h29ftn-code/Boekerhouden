using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Data.Migraties
{
    /// <inheritdoc />
    public partial class Jaarversies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_RekeningPresentaties",
                table: "RekeningPresentaties");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PostPresentaties",
                table: "PostPresentaties");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KlantPostNamen",
                table: "KlantPostNamen");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KantoorPostNamen",
                table: "KantoorPostNamen");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GrondslagSjablonen",
                table: "GrondslagSjablonen");

            migrationBuilder.AddColumn<int>(
                name: "VanafBoekjaar",
                table: "RekeningPresentaties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VanafBoekjaar",
                table: "PostPresentaties",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Naam",
                table: "KlantPostNamen",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddColumn<int>(
                name: "VanafBoekjaar",
                table: "KlantPostNamen",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Naam",
                table: "KantoorPostNamen",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddColumn<int>(
                name: "VanafBoekjaar",
                table: "KantoorPostNamen",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VanafBoekjaar",
                table: "GrondslagSjablonen",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_RekeningPresentaties",
                table: "RekeningPresentaties",
                columns: new[] { "GrootboekrekeningId", "VanafBoekjaar" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_PostPresentaties",
                table: "PostPresentaties",
                columns: new[] { "KlantId", "Sleutel", "VanafBoekjaar" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_KlantPostNamen",
                table: "KlantPostNamen",
                columns: new[] { "KlantId", "Code", "VanafBoekjaar" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_KantoorPostNamen",
                table: "KantoorPostNamen",
                columns: new[] { "Code", "VanafBoekjaar" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_GrondslagSjablonen",
                table: "GrondslagSjablonen",
                columns: new[] { "Sleutel", "VanafBoekjaar" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_RekeningPresentaties",
                table: "RekeningPresentaties");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PostPresentaties",
                table: "PostPresentaties");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KlantPostNamen",
                table: "KlantPostNamen");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KantoorPostNamen",
                table: "KantoorPostNamen");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GrondslagSjablonen",
                table: "GrondslagSjablonen");

            migrationBuilder.DropColumn(
                name: "VanafBoekjaar",
                table: "RekeningPresentaties");

            migrationBuilder.DropColumn(
                name: "VanafBoekjaar",
                table: "PostPresentaties");

            migrationBuilder.DropColumn(
                name: "VanafBoekjaar",
                table: "KlantPostNamen");

            migrationBuilder.DropColumn(
                name: "VanafBoekjaar",
                table: "KantoorPostNamen");

            migrationBuilder.DropColumn(
                name: "VanafBoekjaar",
                table: "GrondslagSjablonen");

            migrationBuilder.AlterColumn<string>(
                name: "Naam",
                table: "KlantPostNamen",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Naam",
                table: "KantoorPostNamen",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_RekeningPresentaties",
                table: "RekeningPresentaties",
                column: "GrootboekrekeningId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PostPresentaties",
                table: "PostPresentaties",
                columns: new[] { "KlantId", "Sleutel" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_KlantPostNamen",
                table: "KlantPostNamen",
                columns: new[] { "KlantId", "Code" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_KantoorPostNamen",
                table: "KantoorPostNamen",
                column: "Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GrondslagSjablonen",
                table: "GrondslagSjablonen",
                column: "Sleutel");
        }
    }
}
