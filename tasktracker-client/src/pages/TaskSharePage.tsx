import { Navigate, useParams } from "react-router-dom";

export default function TaskSharePage() {
  const { taskId } = useParams();
  return <Navigate replace to={`/tasks/task-detail/${encodeURIComponent(taskId ?? "")}?people=invite`} />;
}
