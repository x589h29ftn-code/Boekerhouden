using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Boekerhouden.Administratie.Data.MigratiesSqlite
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Ondernemingsvorm = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    KvkNummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BtwNummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Ibans = table.Column<string>(type: "TEXT", nullable: false),
                    Betalingstermijn = table.Column<int>(type: "INTEGER", nullable: true),
                    LimietAfwijkend = table.Column<bool>(type: "INTEGER", nullable: false),
                    LimietAutomatisch = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    DrempelAutomatisch = table.Column<int>(type: "INTEGER", nullable: true),
                    Bandbreedte = table.Column<long>(type: "INTEGER", precision: 6, scale: 2, nullable: true),
                    Adrescontrole = table.Column<bool>(type: "INTEGER", nullable: true),
                    Documentmap = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    AangemaaktOp = table.Column<long>(type: "INTEGER", nullable: false),
                    AangemaaktDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Administraties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Initialen = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Actief = table.Column<bool>(type: "INTEGER", nullable: false),
                    MoetWachtwoordWijzigen = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<long>(type: "INTEGER", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRegels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tijdstip = table.Column<long>(type: "INTEGER", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Gebruiker = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Actie = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Onderwerp = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRegels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Kantoorinstellingen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DrempelAutomatisch = table.Column<int>(type: "INTEGER", nullable: false),
                    Bandbreedte = table.Column<long>(type: "INTEGER", precision: 6, scale: 2, nullable: false),
                    LimietAutomatisch = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    Betalingstermijn = table.Column<int>(type: "INTEGER", nullable: false),
                    Adrescontrole = table.Column<bool>(type: "INTEGER", nullable: false),
                    Documentmap = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kantoorinstellingen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leerkeuzes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sleutel = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    Postcodes = table.Column<string>(type: "TEXT", nullable: true),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Tijdstip = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leerkeuzes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Boekingen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Volgnummer = table.Column<long>(type: "INTEGER", nullable: false),
                    Dagboek = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Boekstuk = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    GeboektOp = table.Column<long>(type: "INTEGER", nullable: false),
                    GeboektDoorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeboektDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Hash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    VorigeHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false)
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
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
                name: "Documenten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Bestandsnaam = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Inhoudstype = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Grootte = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Herkend = table.Column<string>(type: "TEXT", nullable: true),
                    Melding = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IngelezenOp = table.Column<long>(type: "INTEGER", nullable: false),
                    IngelezenDoor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BoekingId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documenten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documenten_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Regels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Volgorde = table.Column<int>(type: "INTEGER", nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BtwCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Automatisch = table.Column<bool>(type: "INTEGER", nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    NaamBevat = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BedragVan = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    BedragTot = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: true),
                    OmschrijvingBevat = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TegenIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    Richting = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regels_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Rekeningen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RgsCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Functie = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false)
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
                name: "Relaties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Naam = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Ibans = table.Column<string>(type: "TEXT", nullable: false),
                    BtwNummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    KvkNummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Tegenrekening = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AangemaaktOp = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relaties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relaties_Administraties_AdministratieId",
                        column: x => x.AdministratieId,
                        principalTable: "Administraties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
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
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                name: "AspNetUserPasskeys",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "BLOB", maxLength: 1024, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
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
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GebruikerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoekingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Regelnummer = table.Column<int>(type: "INTEGER", nullable: false),
                    Rekeningnummer = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Omschrijving = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BtwCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Factuurnummer = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "OpenstaandePosten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Soort = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Relatie = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Factuurnummer = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Vervaldatum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Bedrag = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Openstaand = table.Column<long>(type: "INTEGER", precision: 18, scale: 2, nullable: false),
                    Betalingskenmerk = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BoekingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenstaandePosten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenstaandePosten_Boekingen_BoekingId",
                        column: x => x.BoekingId,
                        principalTable: "Boekingen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentInhouden",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdministratieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Inhoud = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentInhouden", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentInhouden_Documenten_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documenten",
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
                name: "IX_AspNetUserPasskeys_UserId",
                table: "AspNetUserPasskeys",
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
                name: "IX_Documenten_AdministratieId_Sha256",
                table: "Documenten",
                columns: new[] { "AdministratieId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_Documenten_AdministratieId_Status",
                table: "Documenten",
                columns: new[] { "AdministratieId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Leerkeuzes_AdministratieId_Sleutel_Id",
                table: "Leerkeuzes",
                columns: new[] { "AdministratieId", "Sleutel", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Lidmaatschappen_GebruikerId",
                table: "Lidmaatschappen",
                column: "GebruikerId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenstaandePosten_AdministratieId_Relatie_Factuurnummer",
                table: "OpenstaandePosten",
                columns: new[] { "AdministratieId", "Relatie", "Factuurnummer" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenstaandePosten_BoekingId",
                table: "OpenstaandePosten",
                column: "BoekingId");

            migrationBuilder.CreateIndex(
                name: "IX_Regels_AdministratieId",
                table: "Regels",
                column: "AdministratieId");

            migrationBuilder.CreateIndex(
                name: "IX_Rekeningen_AdministratieId_Nummer",
                table: "Rekeningen",
                columns: new[] { "AdministratieId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relaties_AdministratieId_Code",
                table: "Relaties",
                columns: new[] { "AdministratieId", "Code" },
                unique: true);

            // Boekingen en het wijzigingslog zijn onwijzigbaar, net als in PostgreSQL.
            migrationBuilder.Sql("""
                CREATE TRIGGER boekingen_onwijzigbaar_update BEFORE UPDATE ON "Boekingen"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingen kunnen niet worden gewijzigd of verwijderd.'); END;
                CREATE TRIGGER boekingen_onwijzigbaar_delete BEFORE DELETE ON "Boekingen"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingen kunnen niet worden gewijzigd of verwijderd.'); END;
                CREATE TRIGGER boekingsregels_onwijzigbaar_update BEFORE UPDATE ON "Boekingsregels"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingsregels kunnen niet worden gewijzigd of verwijderd.'); END;
                CREATE TRIGGER boekingsregels_onwijzigbaar_delete BEFORE DELETE ON "Boekingsregels"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in Boekingsregels kunnen niet worden gewijzigd of verwijderd.'); END;
                CREATE TRIGGER auditregels_onwijzigbaar_update BEFORE UPDATE ON "AuditRegels"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in AuditRegels kunnen niet worden gewijzigd of verwijderd.'); END;
                CREATE TRIGGER auditregels_onwijzigbaar_delete BEFORE DELETE ON "AuditRegels"
                BEGIN SELECT RAISE(ABORT, 'Gegevens in AuditRegels kunnen niet worden gewijzigd of verwijderd.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS boekingen_onwijzigbaar_update;
                DROP TRIGGER IF EXISTS boekingen_onwijzigbaar_delete;
                DROP TRIGGER IF EXISTS boekingsregels_onwijzigbaar_update;
                DROP TRIGGER IF EXISTS boekingsregels_onwijzigbaar_delete;
                DROP TRIGGER IF EXISTS auditregels_onwijzigbaar_update;
                DROP TRIGGER IF EXISTS auditregels_onwijzigbaar_delete;
                """);

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys");

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
                name: "DocumentInhouden");

            migrationBuilder.DropTable(
                name: "Kantoorinstellingen");

            migrationBuilder.DropTable(
                name: "Leerkeuzes");

            migrationBuilder.DropTable(
                name: "Lidmaatschappen");

            migrationBuilder.DropTable(
                name: "OpenstaandePosten");

            migrationBuilder.DropTable(
                name: "Regels");

            migrationBuilder.DropTable(
                name: "Rekeningen");

            migrationBuilder.DropTable(
                name: "Relaties");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Documenten");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Boekingen");

            migrationBuilder.DropTable(
                name: "Administraties");
        }
    }
}
