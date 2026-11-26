import type { TaskAttachment } from "../types/taskAttachment";
import type { TaskSubmission } from "../types/taskSubmission";

export function workflowSubmission(workflowSubmissionId: number | null | undefined, submissions: TaskSubmission[]) {
  return submissions.find(item => item.id === workflowSubmissionId);
}

export function deliveryFileIds(files: TaskAttachment[], submissionId: number) {
  return files.filter(file => file.kind === "Submission" && file.taskSubmissionId === submissionId).map(file => file.id);
}

export function sameFiles(expected: readonly number[], current: readonly number[]) {
  return expected.length === current.length && expected.every(id => current.includes(id));
}

