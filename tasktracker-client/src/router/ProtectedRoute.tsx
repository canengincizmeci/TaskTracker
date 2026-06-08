import { Navigate, useLocation } from "react-router-dom";
import { getAccessToken, clearAuthTokens } from "../utils/authStorage";
import { getRoleFromToken, isTokenExpired } from "../utils/jwtHelper";

type ProtectedRouteProps = {
  children: React.ReactNode;
  requiredRole?: string;
};

function ProtectedRoute({ children, requiredRole }: ProtectedRouteProps) {
  const location = useLocation();
  const token = getAccessToken();

  const redirectPath = `${location.pathname}${location.search}`;

  if (!token) {
    return (
      <Navigate
        to={`/login?redirect=${encodeURIComponent(redirectPath)}`}
        replace
      />
    );
  }

  if (isTokenExpired(token)) {
    clearAuthTokens();

    return (
      <Navigate
        to={`/login?redirect=${encodeURIComponent(redirectPath)}`}
        replace
      />
    );
  }

  if (requiredRole) {
    const role = getRoleFromToken(token);

    if (role !== requiredRole) {
      return <Navigate to="/" replace />;
    }
  }

  return <>{children}</>;
}

export default ProtectedRoute;