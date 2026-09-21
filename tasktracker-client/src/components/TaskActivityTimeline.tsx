import type { TaskActivity } from "../types/taskWorkspace";

function activityText(activity: TaskActivity): string {
  const actor = activity.actorUserName;
  const target = activity.targetUserName ?? "a collaborator";
  switch (activity.activityType) {
    case "TaskCreated": return `${actor} created the task`;
    case "TaskDetailsUpdated": return `${actor} updated task details`;
    case "UserInvited": return `${actor} invited ${target}`;
    case "InvitationAccepted": return `${actor} accepted the invitation`;
    case "InvitationRejected": return `${actor} rejected the invitation`;
    case "InvitationCancelled": return `${actor} cancelled the invitation for ${target}`;
    case "UserAssigned": return `${actor} assigned ${target}`;
    case "UserUnassigned": return `${actor} unassigned ${target}`;
    case "WorkStarted": return `${actor} started work`;
    case "TaskCompleted": return `${actor} completed the task`;
    case "TaskCancelled": return `${actor} cancelled the task`;
    case "TaskReopened": return `${actor} reopened the task`;
    case "ParticipantRemoved": return `${actor} removed ${target}`;
    case "ParticipantPermissionChanged": return `${actor} changed ${target}'s permission`;
    default: return `${actor}: ${activity.activityType}`;
  }
}

export default function TaskActivityTimeline({ activities, loading, error }: {
  activities: TaskActivity[];
  loading: boolean;
  error: string;
}) {
  return <section className="task-detail-section" aria-label="Activity / Work History">
    <h2>Activity / Work History</h2>
    <p>Latest 100 events, oldest first.</p>
    {error && <p role="alert" className="error-message">{error}</p>}
    {loading && <p>Loading activity...</p>}
    {!loading && !error && activities.length === 0 && <p>No recorded activity yet.</p>}
    <ol className="activity-timeline">
      {activities.map((activity) => <li className="timeline-item" key={activity.id}>
        <span>{activityText(activity)}</span>
        <time dateTime={activity.createdAt}>{new Date(activity.createdAt).toLocaleString()}</time>
      </li>)}
    </ol>
  </section>;
}
