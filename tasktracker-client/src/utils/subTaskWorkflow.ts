export function subTaskLink(taskId: number, subTaskId: number) {
  return `/tasks/task-detail/${taskId}?subtask=${subTaskId}`;
}

export function parentAllowsSubTaskChanges(status: string) {
  return status === "Pending" || status === "InProgress";
}

export function isOpenSubTask(status: string) {
  return status === "Pending" || status === "InProgress" || status === "InReview";
}
