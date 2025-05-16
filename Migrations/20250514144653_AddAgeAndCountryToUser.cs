using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IqTest_server.Migrations
{
    /// <inheritdoc />
    public partial class AddAgeAndCountryToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Age",
                table: "Users",
                type: "int",
                nullable: true,
                defaultValue: null);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "United States");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Age",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Users");
        }
    }
}