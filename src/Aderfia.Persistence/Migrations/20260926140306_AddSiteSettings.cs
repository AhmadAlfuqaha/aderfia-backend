using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aderfia.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HomeHeroImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HomeHeroProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteSettings_ProductImages_HomeHeroImageId",
                        column: x => x.HomeHeroImageId,
                        principalTable: "ProductImages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteSettings_Products_HomeHeroProductId",
                        column: x => x.HomeHeroProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_HomeHeroImageId",
                table: "SiteSettings",
                column: "HomeHeroImageId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_HomeHeroProductId",
                table: "SiteSettings",
                column: "HomeHeroProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteSettings");
        }
    }
}
