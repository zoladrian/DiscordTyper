using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Rounds_SeasonId_Number",
                table: "Rounds",
                columns: new[] { "SeasonId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_MatchId",
                table: "Predictions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_StartTime",
                table: "Matches",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Status",
                table: "Matches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ThreadId",
                table: "Matches",
                column: "ThreadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rounds_SeasonId_Number",
                table: "Rounds");

            migrationBuilder.DropIndex(
                name: "IX_Predictions_MatchId",
                table: "Predictions");

            migrationBuilder.DropIndex(
                name: "IX_Matches_StartTime",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_Status",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_ThreadId",
                table: "Matches");
        }
    }
}
