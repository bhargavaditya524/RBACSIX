using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RBACSIX.Migrations
{
    /// <inheritdoc />
    public partial class QuizAndDoubtSolving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quizzes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    CreatedByStaffUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quizzes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quizzes_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentDoubts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Solution = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnsweredByStaffUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnsweredUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentDoubts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentDoubts_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentDoubts_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuizId = table.Column<int>(type: "int", nullable: false),
                    StudentProfileId = table.Column<int>(type: "int", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSavedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalScore = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GradedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuizId = table.Column<int>(type: "int", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Marks = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestions_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestionOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuizQuestionId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestionOptions_QuizQuestions_QuizQuestionId",
                        column: x => x.QuizQuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuizAttemptId = table.Column<int>(type: "int", nullable: false),
                    QuizQuestionId = table.Column<int>(type: "int", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "int", nullable: true),
                    WrittenResponse = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Score = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Feedback = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnsweredUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizAttempts_QuizAttemptId",
                        column: x => x.QuizAttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizQuestionOptions_SelectedOptionId",
                        column: x => x.SelectedOptionId,
                        principalTable: "QuizQuestionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuizAnswers_QuizQuestions_QuizQuestionId",
                        column: x => x.QuizQuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_QuizAttemptId_QuizQuestionId",
                table: "QuizAnswers",
                columns: new[] { "QuizAttemptId", "QuizQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_QuizQuestionId",
                table: "QuizAnswers",
                column: "QuizQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAnswers_SelectedOptionId",
                table: "QuizAnswers",
                column: "SelectedOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_QuizId_StudentProfileId",
                table: "QuizAttempts",
                columns: new[] { "QuizId", "StudentProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_StudentProfileId",
                table: "QuizAttempts",
                column: "StudentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestionOptions_QuizQuestionId_Order",
                table: "QuizQuestionOptions",
                columns: new[] { "QuizQuestionId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_QuizId_Order",
                table: "QuizQuestions",
                columns: new[] { "QuizId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quizzes_BatchId_CreatedUtc",
                table: "Quizzes",
                columns: new[] { "BatchId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentDoubts_BatchId_StudentProfileId_CreatedUtc",
                table: "StudentDoubts",
                columns: new[] { "BatchId", "StudentProfileId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentDoubts_StudentProfileId",
                table: "StudentDoubts",
                column: "StudentProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAnswers");

            migrationBuilder.DropTable(
                name: "StudentDoubts");

            migrationBuilder.DropTable(
                name: "QuizAttempts");

            migrationBuilder.DropTable(
                name: "QuizQuestionOptions");

            migrationBuilder.DropTable(
                name: "QuizQuestions");

            migrationBuilder.DropTable(
                name: "Quizzes");
        }
    }
}
