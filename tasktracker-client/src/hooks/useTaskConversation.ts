import { createContext, useContext } from "react";
import type { TaskActivity, TaskMessage } from "../types/taskCollaboration";

export const ConversationContext = createContext<{
  taskId: number;
  currentUserId: number | null;
  messages: TaskMessage[];
  activities: TaskActivity[];
  loading: boolean;
  activityError: string;
  messageError: string;
  liveError: string;
  rosterRevision: number;
  taskRevision: number;
  onAccessRevoked: () => void;
  reload: () => Promise<void>;
  addMessage: (message: TaskMessage) => void;
} | null>(null);

export function useTaskConversation() {
  const value = useContext(ConversationContext);
  if (!value) throw new Error("A task workspace is required to load its conversation.");
  return value;
}
