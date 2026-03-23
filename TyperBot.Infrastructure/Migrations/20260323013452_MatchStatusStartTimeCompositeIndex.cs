using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MatchStatusStartTimeCompositeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status_StartTime",
                table: "Matches",
                columns: new[] { "Status", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_Status_StartTime",
                table: "Matches");
        }
    }
}
