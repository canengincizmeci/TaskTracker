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

type RefreshTokenRequest = {
  refreshToken: string;
};

type RefreshTokenResponse = {
  data: {
    accessToken: string;
    accessTokenExpiration: string;
    refreshToken: string;
  };
  success: boolean;
  message: string | null;
};

type ForgotPasswordRequest = {
  email: string;
};

type VerifyPasswordResetCodeRequest = {
  email: string;
  code: string;
};

type PasswordResetToken = {
  resetToken: string;
  expiresAt: string;
};

type VerifyPasswordResetCodeResponse = {
  data: PasswordResetToken;
  success: boolean;
  message: string;
};

type ResetPasswordRequest = {
  resetToken: string;
  newPassword: string;
  confirmNewPassword: string;
};

type ChangePasswordRequest = {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
};

type ResultResponse = {
  success: boolean;
  message: string;
};

async function refreshToken(
  data: RefreshTokenRequest
): Promise<RefreshTokenResponse> {
  const response = await axiosClient.post("/Auth/refresh-token", data);
  return response.data;
}

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

async function forgotPassword(
  data: ForgotPasswordRequest
): Promise<ResultResponse> {
  const response = await axiosClient.post("/Auth/forgot-password", data);
  return response.data;
}

async function verifyPasswordResetCode(
  data: VerifyPasswordResetCodeRequest
): Promise<VerifyPasswordResetCodeResponse> {
  const response = await axiosClient.post(
    "/Auth/verify-password-reset-code",
    data
  );
  return response.data;
}

async function resetPassword(
  data: ResetPasswordRequest
): Promise<ResultResponse> {
  const response = await axiosClient.post("/Auth/reset-password", data);
  return response.data;
}

async function changePassword(
  data: ChangePasswordRequest
): Promise<ResultResponse> {
  const response = await axiosClient.post("/Auth/change-password", data);
  return response.data;
}

export {
  login,
  register,
  verifyEmail,
  refreshToken,
  forgotPassword,
  verifyPasswordResetCode,
  resetPassword,
  changePassword,
};
