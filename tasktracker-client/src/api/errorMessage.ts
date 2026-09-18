import { isAxiosError } from "axios";

export function errorMessage(error: unknown, fallback: string): string {
  if (!isAxiosError(error)) return fallback;
  const data: unknown = error.response?.data;
  if (typeof data === "string" && data.trim()) return data;
  if (data && typeof data === "object") {
    const details = data as Record<string, unknown>;
    for (const key of ["message", "title"] as const) {
      if (typeof details[key] === "string") return details[key];
    }
  }
  return fallback;
}
