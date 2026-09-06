import { Navigate, useLocation } from "react-router-dom";
import LoginPage from "../pages/LoginPage";
import { getAccessToken } from "../utils/authStorage";
import { getRoleFromToken, isTokenExpired } from "../utils/jwtHelper";
import { getSafeLoginRedirect } from "../utils/loginRedirect";

function LoginRoute() {
  const location = useLocation();
  const token = getAccessToken();

  if (!token || isTokenExpired(token)) {
    return <LoginPage />;
  }

  const searchParams = new URLSearchParams(location.search);
  const redirect = getSafeLoginRedirect(searchParams.get("redirect"));
  const destination =
    redirect ??
    (getRoleFromToken(token) === "Admin"
      ? "/admin-dashboard"
      : "/tasks/user-tasks");

  return <Navigate to={destination} replace />;
}

export default LoginRoute;
