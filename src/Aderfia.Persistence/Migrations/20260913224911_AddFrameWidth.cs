using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aderfia.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFrameWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Dimensions_FrameWidth",
                table: "ProductVariants",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Dimensions_FrameWidth",
                table: "Products",
                type: "TEXT",
                precision: 9,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dimensions_FrameWidth",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Dimensions_FrameWidth",
                table: "Products");
        }
    }
}
