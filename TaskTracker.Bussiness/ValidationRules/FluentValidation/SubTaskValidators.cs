using FluentValidation;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Core.Utilities.Enums;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

internal static class SubTaskValidators
{
    internal static readonly IValidator<CreateSubTaskDto> Details = new DetailsValidator();
    internal static readonly IValidator<SubTaskCommandDto> Command = new CommandValidator();
    internal static readonly IValidator<AssignSubTaskDto> Assignment = new AssignmentValidator();
    internal static readonly IValidator<CreateSubTaskSubmissionDto> Submission = new SubmissionValidator();
    internal static readonly IValidator<ReviewSubTaskSubmissionDto> Review = new ReviewValidator();

    private sealed class DetailsValidator : AbstractValidator<CreateSubTaskDto>
    {
        public DetailsValidator()
        {
            RuleFor(x => x.Title).Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= SubTask.MaxTitleLength)
                .WithMessage($"A subtask title must contain 1–{SubTask.MaxTitleLength} characters.");
            RuleFor(x => x.Description)
                .Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= SubTask.MaxDescriptionLength)
                .WithMessage($"A subtask description must contain 1–{SubTask.MaxDescriptionLength} characters.");
            RuleFor(x => x.Priority).IsInEnum();
            RuleFor(x => x.UtcOffsetMinutes).InclusiveBetween(TaskCalendar.MinOffsetMinutes, TaskCalendar.MaxOffsetMinutes);
        }
    }

    private sealed class CommandValidator : AbstractValidator<SubTaskCommandDto>
    {
        public CommandValidator() => RuleFor(x => x.Version).GreaterThanOrEqualTo(0)
            .WithMessage("A nonnegative subtask version is required.");
    }

    private sealed class AssignmentValidator : AbstractValidator<AssignSubTaskDto>
    {
        public AssignmentValidator() => RuleFor(x => x.AssigneeUserId).GreaterThan(0);
    }

    private sealed class SubmissionValidator : AbstractValidator<CreateSubTaskSubmissionDto>
    {
        public SubmissionValidator() => RuleFor(x => x.Content)
            .Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= SubTaskSubmission.MaxContentLength)
            .WithMessage($"A submission must contain 1–{SubTaskSubmission.MaxContentLength} characters.");
    }

    private sealed class ReviewValidator : AbstractValidator<ReviewSubTaskSubmissionDto>
    {
        public ReviewValidator()
        {
            RuleFor(x => x.Decision).IsInEnum();
            RuleFor(x => x.Feedback).Must(x => x is null || x.Trim().Length <= SubTaskSubmissionReview.MaxFeedbackLength)
                .WithMessage($"Feedback may contain at most {SubTaskSubmissionReview.MaxFeedbackLength} characters.");
            RuleFor(x => x.Feedback).NotEmpty().When(x => x.Decision == TaskReviewDecision.ChangesRequested)
                .WithMessage("Changes requested requires nonblank feedback.");
        }
    }
}
