using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RBACSIX.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledZoomClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    CreatedByStaffUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    StartTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ZoomMeetingId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    JoinUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledClasses_AspNetUsers_CreatedByStaffUserId",
                        column: x => x.CreatedByStaffUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledClasses_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledClasses_BatchId",
                table: "ScheduledClasses",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledClasses_CreatedByStaffUserId",
                table: "ScheduledClasses",
                column: "CreatedByStaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledClasses_ZoomMeetingId",
                table: "ScheduledClasses",
                column: "ZoomMeetingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledClasses");
        }
    }
}
