import { Navigate } from "react-router-dom";
import { getAccessToken } from "../utils/authStorage";
import { getRoleFromToken, isTokenExpired } from "../utils/jwtHelper";

type GuestRouteProps = {
  children: React.ReactNode;
};

function GuestRoute({ children }: GuestRouteProps) {
  const token = getAccessToken();

  if (!token || isTokenExpired(token)) {
    return <>{children}</>;
  }

  const destination =
    getRoleFromToken(token) === "Admin"
      ? "/admin-dashboard"
      : "/tasks/user-tasks";

  return <Navigate to={destination} replace />;
}

export default GuestRoute;
