import axios from "axios";

const axiosClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
});

axiosClient.interceptors.request.use((config) => {
  const token = sessionStorage.getItem("adminToken");

  if (token) {
    config.headers["X-Admin-Token"] = token;
  }

  return config;
});

export default axiosClient;