using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentsAndPrebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrebuildId",
                table: "WorkItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPrebuild",
                table: "Workflows",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WorkflowAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowId = table.Column<int>(type: "int", nullable: false),
                    StaffNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAssignments_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignments_WorkflowId_StaffNumber",
                table: "WorkflowAssignments",
                columns: new[] { "WorkflowId", "StaffNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowAssignments");

            migrationBuilder.DropColumn(
                name: "PrebuildId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "RequiresPrebuild",
                table: "Workflows");
        }
    }
}
