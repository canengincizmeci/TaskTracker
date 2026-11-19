namespace TaskTracker.Bussiness.ValidationRules.FluentValidation;

public static class TaskCalendar
{
    public const int MinOffsetMinutes = -12 * 60;
    public const int MaxOffsetMinutes = 14 * 60;

    public static DateOnly LocalDay(DateTime utcNow, int utcOffsetMinutes) =>
        DateOnly.FromDateTime(utcNow.AddMinutes(utcOffsetMinutes));
}
