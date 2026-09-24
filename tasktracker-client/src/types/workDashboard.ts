export type WorkDashboardSummary = {
  assignedToMeCount: number;
  awaitingMyReviewCount: number;
  overdueCount: number;
  pendingInvitationCount: number;
};

export type WorkScope = "all" | "owned" | "assigned" | "shared";

export type WorkDueFilter = "all" | "overdue" | "today" | "soon" | "none";

export type WorkSort = "due" | "priority" | "recent" | "reviewAge";

export type WorkTaskStatus = "Pending" | "InProgress" | "InReview" | "Completed" | "Cancelled";

export type WorkTaskPriority = "Low" | "Medium" | "High" | "Critical";

export type WorkTaskNextAction = "Start" | "Submit" | "Revise" | "Review" | "View";

export type WorkTaskQuery = {
  scope: WorkScope;
  search?: string;
  status?: WorkTaskStatus;
  priority?: WorkTaskPriority;
  due: WorkDueFilter;
  sort: WorkSort;
  page: number;
  pageSize: number;
};

export type WorkTaskListItem = {
  taskId: number;
  version: number;
  title: string;
  description: string;
  category: string;
  status: WorkTaskStatus;
  priority: WorkTaskPriority;
  dueDate: string | null;
  ownerId: number;
  ownerUserName: string;
  assigneeUserId: number | null;
  assigneeUserName: string | null;
  isOwned: boolean;
  isAssigned: boolean;
  isShared: boolean;
  nextAction: WorkTaskNextAction;
  latestSubmissionId: number | null;
  reviewSubmittedAt: string | null;
  createdAt: string;
};

export type PagedWorkTasks = {
  items: WorkTaskListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};
