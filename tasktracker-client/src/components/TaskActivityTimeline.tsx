import type { TaskActivity } from "../types/taskCollaboration";

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
    case "ResponsibilityReset": return `${actor} reset progress to Not started after responsibility changed`;
    case "WorkStarted": return `${actor} started work`;
    case "SubmissionCreated": return `${actor} submitted ${activity.revisionNumber ? `revision ${activity.revisionNumber}` : "work"} for review`;
    case "ChangesRequested": return `${actor} requested changes${activity.revisionNumber ? ` to revision ${activity.revisionNumber}` : ""}`;
    case "SubmissionApproved": return `${actor} approved ${activity.revisionNumber ? `revision ${activity.revisionNumber}` : "the submission"}`;
    case "TaskCompleted": return `${actor} completed the task`;
    case "TaskCancelled": return `${actor} cancelled the task`;
    case "TaskReopened": return `${actor} reopened the task`;
    case "ParticipantRemoved": return `${actor} removed task access for ${target}`;
    case "ParticipantPermissionChanged": return `${actor} changed task access for ${target}`;
    case "TaskDeleted": return `${actor} deleted the task`;
    default: return `${actor} · Task activity`;
  }
}

export default function TaskActivityTimeline({ activities, loading, error }: {
  activities: TaskActivity[];
  loading: boolean;
  error: string;
}) {
  return <section className="tw-activity" aria-labelledby="task-activity-title">
    <h2 id="task-activity-title">Activity</h2>
    {activities.length >= 100 && <p>Showing the latest 100 events.</p>}
    {error && <p role="alert" className="error-message">{error}</p>}
    {loading && <p>Loading activity...</p>}
    {!loading && !error && activities.length === 0 && <p>No recorded activity yet.</p>}
    <ol>
      {activities.slice().reverse().map((activity) => <li key={activity.id}>
        <span>{activityText(activity)}</span>
        <time dateTime={activity.createdAt}>{new Date(activity.createdAt).toLocaleString()}</time>
      </li>)}
    </ol>
  </section>;
}
