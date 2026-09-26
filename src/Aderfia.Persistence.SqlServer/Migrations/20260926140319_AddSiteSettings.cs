using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aderfia.Persistence.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HomeHeroImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HomeHeroProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
