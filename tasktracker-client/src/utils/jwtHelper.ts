type JwtPayload = {
  email?: string;
  exp?: number;
  [key: string]: unknown;
};

const ROLE_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function decodeJwt(token: string): JwtPayload | null {
  try {
    const payload = token.split(".")[1];

    if (!payload) {
      return null;
    }

    const bytes = Uint8Array.from(atob(payload.replace(/-/g, "+").replace(/_/g, "/")), char => char.charCodeAt(0));
    return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch {
    return null;
  }
}

function getRoleFromToken(token: string): string | null {
  const payload = decodeJwt(token);

  if (!payload) {
    return null;
  }

  const role = payload[ROLE_CLAIM];

  if (typeof role === "string") {
    return role;
  }

  return null;
}

function isTokenExpired(token: string): boolean {
  const payload = decodeJwt(token);

  if (!payload || typeof payload.exp !== "number") {
    return true;
  }

  const currentTimeInSeconds = Math.floor(Date.now() / 1000);

  return payload.exp <= currentTimeInSeconds;
}

export { decodeJwt, getRoleFromToken, isTokenExpired };
