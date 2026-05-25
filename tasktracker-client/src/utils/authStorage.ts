// const ACCESS_TOKEN_KEY = "accessToken";
// const REFRESH_TOKEN_KEY = "refreshToken";
// const ACCESS_TOKEN_EXPIRATION_KEY = "accessTokenExpiration";

// function saveAuthTokens(accessToken: string, refreshToken: string, expiration: string) {
//   sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
//   sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
//   sessionStorage.setItem(ACCESS_TOKEN_EXPIRATION_KEY, expiration);
// }

// function getAccessToken() {
//   return sessionStorage.getItem(ACCESS_TOKEN_KEY);
// }

// function getRefreshToken() {
//   return sessionStorage.getItem(REFRESH_TOKEN_KEY);
// }

// function clearAuthTokens() {
//   sessionStorage.removeItem(ACCESS_TOKEN_KEY);
//   sessionStorage.removeItem(REFRESH_TOKEN_KEY);
//   sessionStorage.removeItem(ACCESS_TOKEN_EXPIRATION_KEY);
// }

// function isAuthenticated() {
//   return !!getAccessToken();
// }

// export {
//   saveAuthTokens,
//   getAccessToken,
//   getRefreshToken,
//   clearAuthTokens,
//   isAuthenticated,
// };

const ACCESS_TOKEN_KEY = "accessToken";
const REFRESH_TOKEN_KEY = "refreshToken";
const ACCESS_TOKEN_EXPIRATION_KEY = "accessTokenExpiration";

function saveAuthTokens(
  accessToken: string,
  refreshToken: string,
  expiration: string
) {
  sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  sessionStorage.setItem(ACCESS_TOKEN_EXPIRATION_KEY, expiration);
}

function getAccessToken() {
  return sessionStorage.getItem(ACCESS_TOKEN_KEY);
}

function getRefreshToken() {
  return sessionStorage.getItem(REFRESH_TOKEN_KEY);
}

function getAccessTokenExpiration() {
  return sessionStorage.getItem(ACCESS_TOKEN_EXPIRATION_KEY);
}

function clearAuthTokens() {
  sessionStorage.removeItem(ACCESS_TOKEN_KEY);
  sessionStorage.removeItem(REFRESH_TOKEN_KEY);
  sessionStorage.removeItem(ACCESS_TOKEN_EXPIRATION_KEY);
}

function isAuthenticated() {
  return !!getAccessToken();
}

export {
  saveAuthTokens,
  getAccessToken,
  getRefreshToken,
  getAccessTokenExpiration,
  clearAuthTokens,
  isAuthenticated,
};