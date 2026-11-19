using TaskTracker.Bussiness.ValidationRules.FluentValidation;
using TaskTracker.Core.Entities.Concrete;
using TaskTracker.Entities.DTOs;

namespace TaskTracker.Tests;

public sealed class TaskCalendarTests
{
    [Theory]
    [InlineData(20, 59, 180, 31)]
    [InlineData(21, 0, 180, 1)]
    [InlineData(10, 0, 840, 1)]
    [InlineData(11, 59, -720, 30)]
    [InlineData(12, 0, -720, 31)]
    public void DayUsesOffsetAtTheCurrentInstant(int hour, int minute, int offset, int day)
    {
        var utc = new DateTime(2026, 12, 31, hour, minute, 0, DateTimeKind.Utc);
        var expected = day == 1 ? new DateOnly(2027, 1, 1) : new DateOnly(2026, 12, day);
        Assert.Equal(expected, TaskCalendar.LocalDay(utc, offset));
    }

    [Fact]
    public void CreateAndEditEnforceCategoryAndOffsetBounds()
    {
        var category = new string('ş', TaskRequest.MaxCategoryLength + 1);
        var create = new TaskRequestCreateDto { Title = "Task title", Description = "Description", Category = category, UtcOffsetMinutes = 841 };
        var edit = new UpdateTaskRequestDto { Id = 1, Title = create.Title, Description = create.Description, Category = category, UtcOffsetMinutes = -721 };
        Assert.Equal(new[] { "Category", "UtcOffsetMinutes" }, new TaskRequestCreateDtoValidator().Validate(create).Errors.Select(error => error.PropertyName));
        Assert.Equal(new[] { "Category", "UtcOffsetMinutes" }, new UpdateTaskRequestDtoValidator().Validate(edit).Errors.Select(error => error.PropertyName));
        create.Category = category[..TaskRequest.MaxCategoryLength];
        create.UtcOffsetMinutes = 840;
        Assert.True(new TaskRequestCreateDtoValidator().Validate(create).IsValid);
    }
}
