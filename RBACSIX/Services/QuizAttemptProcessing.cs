using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;
using RBACSIX.Models;

namespace RBACSIX.Services;

public static class QuizAttemptProcessing
{
    public static void Submit(QuizAttempt attempt, DateTime submittedUtc)
    {
        if (attempt.SubmittedUtc.HasValue)
            return;

        foreach (var question in attempt.Quiz.Questions)
        {
            if (question.QuestionType != QuizQuestionTypes.MultipleChoice)
                continue;

            var answer = attempt.Answers.SingleOrDefault(item => item.QuizQuestionId == question.Id);
            if (answer is null)
                continue;

            answer.Score = question.Options.Any(option =>
                option.Id == answer.SelectedOptionId && option.IsCorrect)
                ? question.Marks
                : 0;
        }

        attempt.SubmittedUtc = submittedUtc;
        RecalculateScore(attempt, submittedUtc);
    }

    public static void RecalculateScore(QuizAttempt attempt, DateTime gradedUtc)
    {
        if (!attempt.SubmittedUtc.HasValue)
            return;

        var answersByQuestion = attempt.Answers.ToDictionary(answer => answer.QuizQuestionId);
        var manualAnswersPending = attempt.Quiz.Questions.Any(question =>
            question.QuestionType != QuizQuestionTypes.MultipleChoice &&
            answersByQuestion.TryGetValue(question.Id, out var answer) &&
            HasResponse(answer) &&
            !answer.Score.HasValue);

        if (manualAnswersPending)
        {
            attempt.TotalScore = null;
            attempt.GradedUtc = null;
            return;
        }

        attempt.TotalScore = attempt.Quiz.Questions.Sum(question =>
            answersByQuestion.TryGetValue(question.Id, out var answer)
                ? answer.Score ?? 0
                : 0);
        attempt.GradedUtc = gradedUtc;
    }

    public static async Task<int> FinalizeExpiredAttemptsAsync(
        AppDbContext db,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var expiredAttempts = await db.QuizAttempts
            .Where(attempt => attempt.SubmittedUtc == null && attempt.ExpiresUtc <= nowUtc)
            .Include(attempt => attempt.Quiz)
                .ThenInclude(quiz => quiz.Questions)
                    .ThenInclude(question => question.Options)
            .Include(attempt => attempt.Answers)
            .ToListAsync(cancellationToken);

        foreach (var attempt in expiredAttempts)
            Submit(attempt, attempt.ExpiresUtc);

        if (expiredAttempts.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return expiredAttempts.Count;
    }

    private static bool HasResponse(QuizAnswer answer)
    {
        return !string.IsNullOrWhiteSpace(answer.WrittenResponse) ||
               !string.IsNullOrWhiteSpace(answer.StoredFileName);
    }
}

public sealed class QuizExpirationService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<QuizExpirationService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await QuizAttemptProcessing.FinalizeExpiredAttemptsAsync(
                    db,
                    DateTime.UtcNow,
                    stoppingToken);
            }
            catch (Exception exception) when (
                exception is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
            {
                logger.LogError(exception, "Could not finalize expired quiz attempts.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
