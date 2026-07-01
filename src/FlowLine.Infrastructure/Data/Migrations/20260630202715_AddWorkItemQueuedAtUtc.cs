using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowLine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemQueuedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_CurrentStageId_Status_ClaimedByStationId",
                table: "WorkItems");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueuedAtUtc",
                table: "WorkItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CurrentStageId_Status_ClaimedByStationId_QueuedAtUtc",
                table: "WorkItems",
                columns: new[] { "CurrentStageId", "Status", "ClaimedByStationId", "QueuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_CurrentStageId_Status_ClaimedByStationId_QueuedAtUtc",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                table: "WorkItems");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CurrentStageId_Status_ClaimedByStationId",
                table: "WorkItems",
                columns: new[] { "CurrentStageId", "Status", "ClaimedByStationId" });
        }
    }
}
