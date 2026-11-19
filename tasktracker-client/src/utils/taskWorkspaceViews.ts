export const tabs = ["work", "discussion", "files", "activity"] as const;
export type View = typeof tabs[number];
export const labels = { work: "Work", discussion: "Discussion", files: "Files", activity: "Activity" };
export function getWorkspaceView(location: { search: string; hash: string }, collaborative: boolean): View {
  if (!collaborative) return "work";
  const selected = new URLSearchParams(location.search).get("view");
  if (tabs.some(tab => tab === selected)) return selected as View;
  switch (location.hash) {
    case "#task-discussion": return "discussion";
    case "#task-files": return "files";
    case "#task-history": return "activity";
    default: return "work";
  }
}
