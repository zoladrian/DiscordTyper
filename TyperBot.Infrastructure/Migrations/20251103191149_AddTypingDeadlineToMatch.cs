using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TyperBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypingDeadlineToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TypingDeadline",
                table: "Matches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TypingDeadline",
                table: "Matches");
        }
    }
}
