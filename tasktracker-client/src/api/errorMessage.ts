import { isAxiosError } from "axios";

export function errorMessage(error: unknown, fallback: string): string {
  if (!isAxiosError(error)) return fallback;
  const data: unknown = error.response?.data;
  if (typeof data === "string" && data.trim()) return data;
  if (data && typeof data === "object") {
    const details = data as Record<string, unknown>;
    // FluentValidation middleware returns Newtonsoft/PascalCase, controllers use camelCase.
    if (Array.isArray(details.Errors)) {
      const first = details.Errors.find(item => typeof item?.ErrorMessage === "string" && item.ErrorMessage.trim());
      if (first) return first.ErrorMessage.trim();
    }
    for (const key of ["message", "Message", "title"] as const) {
      if (typeof details[key] === "string" && details[key].trim()) return details[key].trim();
    }
  }
  return fallback;
}
