using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Players_Seasons_SeasonId",
                table: "Players");

            migrationBuilder.DropForeignKey(
                name: "FK_PlayerScores_Players_PlayerId",
                table: "PlayerScores");

            migrationBuilder.DropIndex(
                name: "IX_Players_SeasonId",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "SeasonId",
                table: "Players");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Rounds",
                type: "TEXT",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500);

            // First, update any NULL PlayerId values to use the PlayerId from the related Prediction
            migrationBuilder.Sql(@"
                UPDATE PlayerScores
                SET PlayerId = (
                    SELECT PlayerId 
                    FROM Predictions 
                    WHERE Predictions.Id = PlayerScores.PredictionId
                )
                WHERE PlayerId IS NULL;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "PlayerScores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerScores_Players_PlayerId",
                table: "PlayerScores",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerScores_Players_PlayerId",
                table: "PlayerScores");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Rounds",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PlayerId",
                table: "PlayerScores",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "SeasonId",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SeasonId",
                table: "Players",
                column: "SeasonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Players_Seasons_SeasonId",
                table: "Players",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerScores_Players_PlayerId",
                table: "PlayerScores",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id");
        }
    }
}
