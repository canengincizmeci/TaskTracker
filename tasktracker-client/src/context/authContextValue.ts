import { createContext, useContext } from "react";

export type AuthUser = {
  name: string | null;
  email: string | null;
  role: string | null;
};

export type AuthContextType = {
  token: string | null;
  user: AuthUser | null;
  isAuthenticated: boolean;
  loginToContext: (
    accessToken: string,
    refreshToken: string,
    expiration: string
  ) => void;
  logout: () => void;
};

export const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function useAuth() {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error("useAuth must be used inside AuthProvider");
  }

  return context;
}
