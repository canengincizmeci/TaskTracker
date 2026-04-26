import { Link } from "react-router-dom";
import type { Task } from "../types/task";

type TaskCardProps = {
  task: Task;
};

function TaskCard({ task }: TaskCardProps) {
  return (
    <article className="task-row">
      <div className="task-row-main">
        <div className="task-row-top">
          <span className="task-category">{task.category}</span>
          <span className={`task-status status-${task.status.toLowerCase().replaceAll(" ", "-")}`}>
            {task.status}
          </span>
        </div>

        <h3>{task.title}</h3>

        <p>
          {task.description.length > 150
            ? `${task.description.substring(0, 150)}...`
            : task.description}
        </p>
      </div>

      <div className="task-row-side">
        <span className={`priority-pill priority-${task.priority.toLowerCase()}`}>
          {task.priority}
        </span>

        <Link to={`/task/${task.id}`} className="details-link">
          View details
        </Link>
      </div>
    </article>
  );
}

export default TaskCard;