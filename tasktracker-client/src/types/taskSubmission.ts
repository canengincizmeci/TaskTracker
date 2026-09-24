export interface SubmissionReview {
  id: number;
  reviewerUserId: number;
  reviewerUserName: string;
  decision: "Approved" | "ChangesRequested";
  feedback: string | null;
  createdAt: string;
}

export interface TaskSubmission {
  id: number;
  revisionNumber: number;
  submittedByUserId: number;
  submittedByUserName: string;
  content: string;
  createdAt: string;
  review: SubmissionReview | null;
}

export interface AwaitingReviewTask {
  taskId: number;
  title: string;
  version: number;
  assigneeUserId: number | null;
  assigneeUserName: string | null;
  dueDate: string | null;
  latestSubmissionId: number | null;
  latestRevisionNumber: number | null;
  submittedAt: string | null;
}
