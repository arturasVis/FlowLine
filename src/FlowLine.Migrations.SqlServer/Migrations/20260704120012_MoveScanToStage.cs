using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class MoveScanToStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresScan",
                table: "Steps");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresScan",
                table: "Stages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresScan",
                table: "Stages");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresScan",
                table: "Steps",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
