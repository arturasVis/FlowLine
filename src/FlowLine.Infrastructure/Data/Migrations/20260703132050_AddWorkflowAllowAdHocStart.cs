using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowAllowAdHocStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAdHocStart",
                table: "Workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAdHocStart",
                table: "Workflows");
        }
    }
}
