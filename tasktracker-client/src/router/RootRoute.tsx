import { Navigate } from "react-router-dom";
import HomePage from "../pages/HomePage";
import { getAccessToken } from "../utils/authStorage";
import { getRoleFromToken, isTokenExpired } from "../utils/jwtHelper";

function RootRoute() {
  const token = getAccessToken();

  if (!token || isTokenExpired(token)) {
    return <HomePage />;
  }

  const destination =
    getRoleFromToken(token) === "Admin"
      ? "/admin-dashboard"
      : "/tasks/user-tasks";

  return <Navigate to={destination} replace />;
}

export default RootRoute;
