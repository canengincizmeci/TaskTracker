import axios, { type InternalAxiosRequestConfig } from "axios";
import {
  clearAuthTokens,
  getAccessToken,
  getAuthGeneration,
  getRefreshToken,
  saveAuthTokens,
} from "../utils/authStorage";

const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
});

type SessionRequest = InternalAxiosRequestConfig & { authGeneration?: number; retried?: boolean };
type RefreshResponse = {
  data: { accessToken: string; accessTokenExpiration: string; refreshToken: string };
};

let pendingRefresh: { generation: number; promise: Promise<void> } | undefined;

function refreshSession(generation: number, refreshToken: string): Promise<void> {
  if (pendingRefresh?.generation === generation) return pendingRefresh.promise;

  const promise = axios.post<RefreshResponse>(
    `${import.meta.env.VITE_API_BASE_URL}/Auth/refresh-token`, { refreshToken }
  ).then(({ data: { data } }) => {
    if (getAuthGeneration() !== generation) throw new axios.CanceledError("The session changed.");
    saveAuthTokens(data.accessToken, data.refreshToken, data.accessTokenExpiration);
  }).finally(() => {
    if (pendingRefresh?.promise === promise) pendingRefresh = undefined;
  });
  pendingRefresh = { generation, promise };
  return promise;
}

axiosClient.interceptors.request.use((config: SessionRequest) => {
  const generation = getAuthGeneration();
  if (config.authGeneration !== undefined && config.authGeneration !== generation) {
    throw new axios.CanceledError("The session changed.");
  }
  config.authGeneration = generation;
  const token = getAccessToken();

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  return config;
});

axiosClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config as SessionRequest | undefined;
    const generation = originalRequest?.authGeneration;

    if (error.response?.status === 401 && originalRequest && !originalRequest.retried &&
        generation !== undefined && getAuthGeneration() === generation) {
      originalRequest.retried = true;
      const refreshToken = getRefreshToken();
      if (!refreshToken) {
        clearAuthTokens();
        window.location.href = "/login";
        return Promise.reject(error);
      }

      try {
        const accessToken = getAccessToken();
        if (!accessToken || originalRequest.headers.get("Authorization") === `Bearer ${accessToken}`) {
          await refreshSession(generation, refreshToken);
        }
        return axiosClient(originalRequest);
      } catch (refreshError) {
        if (getAuthGeneration() === generation &&
            !(axios.isAxiosError(refreshError) && refreshError.response?.status === 429)) {
          clearAuthTokens();
          window.location.href = "/login";
        }
        return Promise.reject(refreshError);
      }
    }

    return Promise.reject(error);
  }
);

export default axiosClient;
