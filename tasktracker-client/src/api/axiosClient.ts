import axios from "axios";

const axiosClient = axios.create({
  baseURL: "https://localhost:7074/api"
});

axiosClient.interceptors.request.use((config) => {
  const token = sessionStorage.getItem("adminToken");

  if (token) {
    config.headers["X-Admin-Token"] = token;
  }

  return config;
});

export default axiosClient;