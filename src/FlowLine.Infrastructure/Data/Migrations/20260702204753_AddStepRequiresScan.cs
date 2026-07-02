using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStepRequiresScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresScan",
                table: "Steps",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresScan",
                table: "Steps");
        }
    }
}
