import { useEffect, useRef, useState, type ReactNode } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import {
  clearAuthTokens,
  getAccessToken,
  saveAuthTokens,
} from "../utils/authStorage";
import { decodeJwt, getRoleFromToken, isTokenExpired } from "../utils/jwtHelper";
import { notificationHubConnection } from "../services/signalRService";
import { AuthContext, type AuthUser } from "./authContextValue";

const NAME_CLAIM =
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";

function getUserFromToken(token: string): AuthUser {
  const payload = decodeJwt(token);

  return {
    name: typeof payload?.[NAME_CLAIM] === "string" ? payload[NAME_CLAIM] : null,
    email: typeof payload?.email === "string" ? payload.email : null,
    role: getRoleFromToken(token),
  };
}

function getValidStoredToken() {
  const storedToken = getAccessToken();

  if (!storedToken) {
    return null;
  }

  if (isTokenExpired(storedToken)) {
    clearAuthTokens();
    return null;
  }

  return storedToken;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(getValidStoredToken);
  const connectionOperation = useRef(Promise.resolve());

  const user = token ? getUserFromToken(token) : null;

  const loginToContext = (
    accessToken: string,
    refreshToken: string,
    expiration: string
  ) => {
    if (isTokenExpired(accessToken)) {
      clearAuthTokens();
      setToken(null);
      return;
    }

    saveAuthTokens(accessToken, refreshToken, expiration);
    setToken(accessToken);
  };

  const logout = () => {
    clearAuthTokens();
    setToken(null);
  };

  useEffect(() => {
    if (token) {
      connectionOperation.current = connectionOperation.current
        .then(async () => {
          if (notificationHubConnection.state === HubConnectionState.Disconnected) {
            await notificationHubConnection.start();
          }
        })
        .catch((error) => {
          console.error("Failed to start notification connection:", error);
        });
    }

    return () => {
      connectionOperation.current = connectionOperation.current
        .then(async () => {
          if (notificationHubConnection.state !== HubConnectionState.Disconnected) {
            await notificationHubConnection.stop();
          }
        })
        .catch((error) => {
          console.error("Failed to stop notification connection:", error);
        });
    };
  }, [token]);

  return (
    <AuthContext.Provider
      value={{
        token,
        user,
        isAuthenticated: !!token,
        loginToContext,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}
