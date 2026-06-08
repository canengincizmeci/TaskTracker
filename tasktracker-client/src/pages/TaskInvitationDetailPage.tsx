import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import toast from "react-hot-toast";
import api from "../api/axiosInstance";

function TaskInvitationDetailPage() {
  const { invitationId } = useParams();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(false);

  const acceptInvitation = async () => {
    if (!invitationId) return;

    try {
      setLoading(true);

      await api.post(`/TaskShare/accept-invitation/${invitationId}`);

      toast.success("Invitation accepted");

      navigate("/", { replace: true });
    } catch (error) {
      console.error(error);
      toast.error("Invitation could not be accepted");
    } finally {
      setLoading(false);
    }
  };

  const rejectInvitation = async () => {
    if (!invitationId) return;

    try {
      setLoading(true);

      await api.post(`/TaskShare/reject-invitation/${invitationId}`);

      toast.success("Invitation rejected");

      navigate("/", { replace: true });
    } catch (error) {
      console.error(error);
      toast.error("Invitation could not be rejected");
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="task-detail-page">
      <section className="task-detail-card">
        <p className="eyebrow">TASK INVITATION</p>

        <h1>You have been invited to a task</h1>

        <p className="task-description">
          You can accept this invitation to access the shared task, or reject it
          if you do not want to join.
        </p>

        <div className="task-detail-actions">
          <button
            className="primary-button"
            onClick={acceptInvitation}
            disabled={loading}
          >
            {loading ? "Processing..." : "Accept Invitation"}
          </button>

          <button
            className="secondary-button"
            onClick={rejectInvitation}
            disabled={loading}
          >
            Reject Invitation
          </button>
        </div>
      </section>
    </main>
  );
}

export default TaskInvitationDetailPage;