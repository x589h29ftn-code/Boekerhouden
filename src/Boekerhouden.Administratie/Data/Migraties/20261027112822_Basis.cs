using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Boekerhouden.Administratie.Data.Migraties
{
    /// <inheritdoc />
    public partial class Basis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Administraties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Naam = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ondernemingsvorm = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    KvkNummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BtwNummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AangemaaktOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Administraties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Initialen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Actief = table.Column<bool>(type: "boolean", nullable: false),
                    MoetWachtwoordWijzigen = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRegels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tijdstip = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Gebruiker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Onderwerp = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRegels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Boekingen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volgnummer = table.Column<long>(type: "bigint", nullable: false),
                    Dagboek = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Boekstuk = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GeboektOp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeboektDoorId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeboektDoor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    VorigeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boekingen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boekingen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dagboeken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Naam = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dagboeken", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dagboeken_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rekeningen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Naam = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Soort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RgsCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Functie = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rekeningen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rekeningen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lidmaatschappen",
                columns: table => new
                {
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lidmaatschappen", x => new { x.AdministratieId, x.GebruikerId });
                    table.ForeignKey(
                        name: "FK_Lidmaatschappen_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lidmaatschappen_AspNetUsers_GebruikerId",
                        column: x => x.GebruikerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Boekingsregels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoekingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "uuid", nullable: false),
                    Regelnummer = table.Column<int>(type: "integer", nullable: false),
                    Rekeningnummer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bedrag = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Omschrijving = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BtwCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Relatie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Factuurnummer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boekingsregels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boekingsregels_Boekingen_BoekingId",
                        column: x => x.BoekingId,
                        principalTable: "Boekingen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRegels_AdministratieId",
                table: "AuditRegels",
                column: "AdministratieId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRegels_Tijdstip",
                table: "AuditRegels",
                column: "Tijdstip");

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_AdministratieId_Dagboek_Boekstuk",
                table: "Boekingen",
                columns: new[] { "AdministratieId", "Dagboek", "Boekstuk" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boekingen_AdministratieId_Volgnummer",
                table: "Boekingen",
                columns: new[] { "AdministratieId", "Volgnummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Boekingsregels_AdministratieId_Rekeningnummer",
                table: "Boekingsregels",
                columns: new[] { "AdministratieId", "Rekeningnummer" });

            migrationBuilder.CreateIndex(
                name: "IX_Boekingsregels_BoekingId",
                table: "Boekingsregels",
                column: "BoekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Dagboeken_AdministratieId_Code",
                table: "Dagboeken",
                columns: new[] { "AdministratieId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lidmaatschappen_GebruikerId",
                table: "Lidmaatschappen",
                column: "GebruikerId");

            migrationBuilder.CreateIndex(
                name: "IX_Rekeningen_AdministratieId_Nummer",
                table: "Rekeningen",
                columns: new[] { "AdministratieId", "Nummer" },
                unique: true);

            // Geboekt is geboekt: de database weigert wijzigen en verwijderen van boekingen en logregels, ook buiten de
            // app om (beveiliging §4.3). Corrigeren gaat met een tegenboeking.
            migrationBuilder.Sql("""
                CREATE FUNCTION weiger_wijziging() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Gegevens in % kunnen niet worden gewijzigd of verwijderd.', TG_TABLE_NAME
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$;
                CREATE TRIGGER boekingen_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingen"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging();
                CREATE TRIGGER boekingsregels_onwijzigbaar BEFORE UPDATE OR DELETE ON "Boekingsregels"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging();
                CREATE TRIGGER auditregels_onwijzigbaar BEFORE UPDATE OR DELETE ON "AuditRegels"
                    FOR EACH ROW EXECUTE FUNCTION weiger_wijziging();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS boekingen_onwijzigbaar ON "Boekingen";
                DROP TRIGGER IF EXISTS boekingsregels_onwijzigbaar ON "Boekingsregels";
                DROP TRIGGER IF EXISTS auditregels_onwijzigbaar ON "AuditRegels";
                DROP FUNCTION IF EXISTS weiger_wijziging();
                """);

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditRegels");

            migrationBuilder.DropTable(
                name: "Boekingsregels");

            migrationBuilder.DropTable(
                name: "Dagboeken");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "Lidmaatschappen");

            migrationBuilder.DropTable(
                name: "Rekeningen");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Boekingen");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Administraties");
        }
    }
}
