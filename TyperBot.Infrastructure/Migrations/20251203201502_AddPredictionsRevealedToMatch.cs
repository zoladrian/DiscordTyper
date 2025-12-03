using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionsRevealedToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PredictionsRevealed",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PredictionsRevealed",
                table: "Matches");
        }
    }
}
