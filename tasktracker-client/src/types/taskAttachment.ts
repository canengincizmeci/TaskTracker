export type TaskAttachment = {
  id: number; taskRequestId: number; uploaderUserId: number; uploaderUserName: string;
  kind: "Task" | "Message" | "Submission"; taskMessageId: number | null; taskSubmissionId: number | null;
  fileName: string; contentType: string; sizeBytes: number; createdAt: string; canDelete: boolean;
};
