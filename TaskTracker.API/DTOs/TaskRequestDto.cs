namespace TaskTracker.API.DTOs
{
    public class TaskRequestDto
    {
   
        public string Title { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Priority { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    }
}
