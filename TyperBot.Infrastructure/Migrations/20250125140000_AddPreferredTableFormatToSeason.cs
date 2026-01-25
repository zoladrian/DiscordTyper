using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredTableFormatToSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreferredTableFormat",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredTableFormat",
                table: "Seasons");
        }
    }
}
