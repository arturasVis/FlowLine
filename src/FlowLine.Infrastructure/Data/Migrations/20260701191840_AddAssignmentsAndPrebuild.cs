using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Infrastructure.Data.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPrebuild",
                table: "Workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WorkflowAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowId = table.Column<int>(type: "INTEGER", nullable: false),
                    StaffNumber = table.Column<int>(type: "INTEGER", nullable: false)
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
