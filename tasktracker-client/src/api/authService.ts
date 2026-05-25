import axiosClient from "./axiosClient";

type LoginRequest = {
  email: string;
  password: string;
};

type RegisterRequest = {
  firstName: string;
  lastName: string;
  userName:string;
  email: string;
  password: string;
};

type LoginResponse = {
  data: {
    accessToken: {
      token: string;
      expiration: string;
    };
    refreshToken: string;
  };
  success: boolean;
  message: string;
};

type VerifyEmailRequest = {
  email: string;
  code: string;
};

async function login(data: LoginRequest): Promise<LoginResponse> {
  const response = await axiosClient.post("/Auth/login", data);
  return response.data;
}

async function register(data: RegisterRequest) {
  const response = await axiosClient.post("/Auth/register", data);
  return response.data;
}


async function verifyEmail(data: VerifyEmailRequest) {
  const response = await axiosClient.post("/Auth/verify-email", data);
  return response.data;
}
export { login, register, verifyEmail };