export function taskStatusLabel(status: string): string {
  switch (status) {
    case "Pending": return "Not started";
    case "InProgress": return "In progress";
    case "InReview": return "Waiting for review";
    case "Completed": return "Completed";
    case "Cancelled": return "Cancelled";
    default: return status;
  }
}
