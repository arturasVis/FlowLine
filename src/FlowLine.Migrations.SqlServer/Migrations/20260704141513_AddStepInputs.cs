using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddStepInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StepInputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StepInputs_Steps_StepId",
                        column: x => x.StepId,
                        principalTable: "Steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StepCompletionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepCompletionId = table.Column<int>(type: "int", nullable: false),
                    StepInputId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepCompletionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StepCompletionValues_StepCompletions_StepCompletionId",
                        column: x => x.StepCompletionId,
                        principalTable: "StepCompletions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StepCompletionValues_StepInputs_StepInputId",
                        column: x => x.StepInputId,
                        principalTable: "StepInputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StepCompletionValues_StepCompletionId",
                table: "StepCompletionValues",
                column: "StepCompletionId");

            migrationBuilder.CreateIndex(
                name: "IX_StepCompletionValues_StepInputId",
                table: "StepCompletionValues",
                column: "StepInputId");

            migrationBuilder.CreateIndex(
                name: "IX_StepInputs_StepId",
                table: "StepInputs",
                column: "StepId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StepCompletionValues");

            migrationBuilder.DropTable(
                name: "StepInputs");
        }
    }
}
