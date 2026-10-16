import { useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { changePassword } from "../api/authService";
import AccountLocalNav from "../components/AccountLocalNav";
import { useAuth } from "../context/AuthContext";

type InvalidPasswordFields = {
  current: boolean;
  next: boolean;
  confirmation: boolean;
};

const noInvalidPasswordFields: InvalidPasswordFields = {
  current: false,
  next: false,
  confirmation: false,
};

function SecuritySettingsPage() {
  const navigate = useNavigate();
  const { logout } = useAuth();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmNewPassword, setConfirmNewPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [invalidFields, setInvalidFields] = useState<InvalidPasswordFields>(
    noInvalidPasswordFields
  );

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError("");
    setInvalidFields(noInvalidPasswordFields);

    if (
      !currentPassword ||
      !newPassword ||
      !confirmNewPassword
    ) {
      setError("Current password, new password and confirmation are required.");
      setInvalidFields({
        current: !currentPassword,
        next: !newPassword,
        confirmation: !confirmNewPassword,
      });
      return;
    }

    if (
      newPassword.length < 12 ||
      newPassword.length > 128 ||
      confirmNewPassword.length < 12 ||
      confirmNewPassword.length > 128
    ) {
      setError("Password must be between 12 and 128 characters.");
      setInvalidFields({
        current: false,
        next: newPassword.length < 12 || newPassword.length > 128,
        confirmation:
          confirmNewPassword.length < 12 || confirmNewPassword.length > 128,
      });
      return;
    }

    if (newPassword !== confirmNewPassword) {
      setError("New password and confirmation do not match.");
      setInvalidFields({ current: false, next: true, confirmation: true });
      return;
    }

    try {
      setLoading(true);

      const response = await changePassword({
        currentPassword,
        newPassword,
        confirmNewPassword,
      });

      setCurrentPassword("");
      setNewPassword("");
      setConfirmNewPassword("");
      toast.success(response.message);
      logout();
      navigate("/login", { replace: true });
    } catch (requestError: unknown) {
      const message = axios.isAxiosError<{ message?: string }>(requestError)
        ? requestError.response?.data?.message
        : undefined;

      setInvalidFields(noInvalidPasswordFields);
      setError(message ?? "Password could not be changed.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="page public-page account-page">
      <div className="account-shell account-shell--security">
        <header className="account-header">
          <div>
            <p className="eyebrow">ACCOUNT</p>
            <h1>Security</h1>
            <p>Manage the password used to sign in to your account.</p>
          </div>
        </header>

        <AccountLocalNav />

        <section className="account-section" aria-labelledby="change-password-title">
          <div className="account-section__heading">
            <div>
              <p className="eyebrow">PASSWORD</p>
              <h2 id="change-password-title">Change password</h2>
              <p>After a successful change, you will be signed out and asked to sign in again.</p>
            </div>
          </div>

          <form onSubmit={handleSubmit} className="account-form" aria-busy={loading} noValidate>
            <div className="account-field">
              <label htmlFor="current-password">Current password</label>
              <input
                id="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                type="password"
                autoComplete="current-password"
                aria-describedby={invalidFields.current
                  ? "current-password-help password-validation-error"
                  : "current-password-help"}
                aria-invalid={invalidFields.current || undefined}
                required
              />
              <span id="current-password-help">Enter the password you currently use to sign in.</span>
            </div>

            <div className="account-password-fields">
              <div className="account-field">
                <label htmlFor="security-new-password">New password</label>
                <input
                  id="security-new-password"
                  value={newPassword}
                  onChange={(event) => setNewPassword(event.target.value)}
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  maxLength={128}
                  aria-describedby={invalidFields.next
                    ? "password-requirements password-validation-error"
                    : "password-requirements"}
                  aria-invalid={invalidFields.next || undefined}
                  required
                />
              </div>

              <div className="account-field">
                <label htmlFor="security-confirm-new-password">
                  Confirm new password
                </label>
                <input
                  id="security-confirm-new-password"
                  value={confirmNewPassword}
                  onChange={(event) => setConfirmNewPassword(event.target.value)}
                  type="password"
                  autoComplete="new-password"
                  minLength={12}
                  maxLength={128}
                  aria-describedby={invalidFields.confirmation
                    ? "password-requirements password-validation-error"
                    : "password-requirements"}
                  aria-invalid={invalidFields.confirmation || undefined}
                  required
                />
              </div>
            </div>

            <p id="password-requirements" className="account-form__hint">
              Your new password must contain between 12 and 128 characters.
            </p>

            {error && <p id="password-validation-error" className="account-alert" role="alert">{error}</p>}

            <div className="account-form__actions">
              <button className="primary-button" type="submit" disabled={loading}>
                {loading ? "Changing password..." : "Change password"}
              </button>
              <Link to="/profile" className="secondary-button">Back to profile</Link>
            </div>
          </form>
        </section>
      </div>
    </main>
  );
}

export default SecuritySettingsPage;
