import axiosClient from "./axiosClient";

type AdminLoginRequest = {
  username: string;
  password: string;
};

type VerifyOtpRequest = {
  username: string;
  code: string;
};

type VerifyOtpResponse = {
  message: string;
  adminToken: string;
  expireAt: string;
};

async function loginAdmin(data: AdminLoginRequest): Promise<string> {
  const response = await axiosClient.post("/Admin/login", data);
  return response.data;
}

async function verifyOtp(data: VerifyOtpRequest): Promise<VerifyOtpResponse> {
  const response = await axiosClient.post("/Admin/verify-otp", data);
  return response.data;
}

export { loginAdmin, verifyOtp };