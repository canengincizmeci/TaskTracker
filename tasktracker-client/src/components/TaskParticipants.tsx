import { useEffect, useState } from "react";
import axiosClient from "../api/axiosClient";
import { errorMessage } from "../api/errorMessage";
import { permissionText, type TaskPermission } from "../types/taskPermission";

type Participant = {
  userId: number;
  userName: string;
  permission: TaskPermission | null;
  sharedAt: string | null;
};

export default function TaskParticipants({ taskId }: { taskId: number }) {
  const [participants, setParticipants] = useState<Participant[] | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let cancelled = false;
    axiosClient.get<Participant[]>(`/TaskShare/task-participants/${taskId}`).then(({ data }) => {
      if (!cancelled) setParticipants(data);
    }).catch((reason: unknown) => {
      if (!cancelled) setError(errorMessage(reason, "Could not load participants."));
    });
    return () => { cancelled = true; };
  }, [taskId]);

  return <section className="task-detail-section" aria-label="Participants">
    <h2>Participants</h2>
    {error && <p role="alert" className="error-message">{error}</p>}
    {!participants && !error && <p>Loading participants...</p>}
    {participants?.length === 0 && <p>No accepted collaborators yet.</p>}
    <ul>{participants?.map((participant) => <li key={participant.userId}>
      <strong>{participant.userName}</strong> — {permissionText(participant.permission)}
      {participant.sharedAt && <span> · Joined {new Date(participant.sharedAt).toLocaleString()}</span>}
    </li>)}</ul>
  </section>;
}
