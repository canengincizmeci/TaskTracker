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

export function matchingDeliveryFileIds(files: TaskAttachment[], submissionId: number, uploaderId: number,
  name: string, size: number) {
  return files.filter(file => file.kind === "Submission" && file.taskSubmissionId === submissionId &&
    file.uploaderUserId === uploaderId && file.fileName === name.trim() && file.sizeBytes === size).map(file => file.id);
}
