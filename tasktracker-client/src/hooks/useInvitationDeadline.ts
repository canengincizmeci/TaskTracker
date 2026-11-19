import { useEffect, useEffectEvent } from "react";

type InvitationState = { status: string; expiresAt?: string | null; respondedAt?: string | null };

function pendingDeadline(invitation: InvitationState) {
  return invitation.status.toLowerCase() === "pending" && invitation.expiresAt
    ? Date.parse(invitation.expiresAt) : Infinity;
}

export function invitationStatus(invitation: InvitationState, now = Date.now()) {
  // Older outgoing responses could omit the decision after expiry. Keep that uncertainty explicit.
  if (invitation.status === "Expired" && invitation.respondedAt) return "Responded";
  return pendingDeadline(invitation) <= now ? "Expired" : invitation.status;
}

export function useInvitationDeadline(items: InvitationState[], onExpiry: () => void) {
  const expire = useEffectEvent(onExpiry);
  useEffect(() => {
    const now = Date.now();
    const next = Math.min(...items.map(pendingDeadline).filter(deadline => deadline > now));
    if (!Number.isFinite(next)) return;
    let timer: number;
    const schedule = () => {
      const remaining = next - Date.now();
      if (remaining <= 0) expire();
      else timer = window.setTimeout(schedule, Math.min(remaining, 2147483647));
    };
    schedule();
    return () => window.clearTimeout(timer);
  }, [items]);
}
