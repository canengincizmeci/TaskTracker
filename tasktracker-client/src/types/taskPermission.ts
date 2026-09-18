export type TaskPermission = "View" | "Edit" | "Manage";

export function permissionText(permission: unknown): string {
  return permission === "View" || permission === "Edit" || permission === "Manage"
    ? permission
    : "Invalid permission — contact the task owner";
}
