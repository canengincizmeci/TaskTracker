export interface TaskActivity {
  id: number;
  activityType: string;
  actorUserId: number;
  actorUserName: string;
  targetUserId: number | null;
  targetUserName: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  createdAt: string;
}

export interface TaskMessage {
  id: number;
  senderUserId: number;
  senderUserName: string;
  content: string;
  createdAt: string;
}
