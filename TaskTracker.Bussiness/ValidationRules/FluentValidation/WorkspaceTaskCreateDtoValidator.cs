using FluentValidation;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

public class WorkspaceTaskCreateDtoValidator : AbstractValidator<WorkspaceTaskCreateDto>
{
    public WorkspaceTaskCreateDtoValidator()
    {
        Include(new TaskRequestCreateDtoValidator());
        RuleFor(x => x.AssigneeUserId).GreaterThan(0).When(x => x.AssigneeUserId.HasValue);
    }
}
