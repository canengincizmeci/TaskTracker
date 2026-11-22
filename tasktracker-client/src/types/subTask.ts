export const subTaskPriorities = ["Low", "Medium", "High", "Critical"] as const;
type SubTaskPriority = typeof subTaskPriorities[number];

export type SubTaskStatus = "Pending" | "InProgress" | "InReview" | "Completed" | "Cancelled";

export type SubTaskFields = {
  title: string;
  description: string;
  priority: SubTaskPriority;
  dueDate: string | null;
};

export type SubTaskCapabilities = {
  canEdit: boolean;
  canDelete: boolean;
  canAssign: boolean;
  canStart: boolean;
  canComplete: boolean;
  canSubmit: boolean;
  canReview: boolean;
  canCancel: boolean;
  canReopen: boolean;
};

export type SubTask = SubTaskFields & {
  id: number;
  taskRequestId: number;
  createdByUserId: number;
  assigneeUserId: number | null;
  assigneeUserName: string | null;
  version: number;
  status: SubTaskStatus;
  createdAt: string;
  currentSubmissionId: number | null;
  capabilities: SubTaskCapabilities | null;
};

export type SubTaskDecision = "Approved" | "ChangesRequested";

export type SubTaskSubmission = {
  id: number;
  revisionNumber: number;
  submittedByUserId: number;
  submittedByUserName: string | null;
  content: string;
  createdAt: string;
  review: {
    id: number;
    reviewerUserId: number;
    reviewerUserName: string | null;
    decision: SubTaskDecision;
    feedback: string | null;
    createdAt: string;
  } | null;
};

export type SubTaskActivity = {
  id: number;
  actorUserName: string | null;
  targetUserName: string | null;
  activityType: string;
  createdAt: string;
  submissionId: number | null;
  fromStatus: SubTaskStatus | null;
  toStatus: SubTaskStatus | null;
};

