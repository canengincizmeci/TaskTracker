// import { useEffect, useState } from "react";
// import { useLocation, useNavigate } from "react-router-dom";

// import type { Task } from "../types/task";

// import toast from "react-hot-toast";

// type LocationState = {
//   successMessage?: string;
// };

// function UserTasksPage() {
//   const navigate = useNavigate();
//   const location = useLocation();

//   const [tasks, setTasks] = useState<Task[]>([]);
//   const [loading, setLoading] = useState(true);
//   const [error, setError] = useState("");

//   useEffect(() => {
//     const state = location.state as LocationState | null;

//     if (state?.successMessage) {
//       toast.success(state.successMessage);

//       navigate(location.pathname, {
//         replace: true,
//         state: null,
//       });
//     }
//   }, [location, navigate]);

//   useEffect(() => {
//     const loadTasks = async () => {
//       try {
//         setLoading(true);

//         setTasks([]);
//       } catch (err) {
//         console.error(err);

//         setError("Tasks could not be loaded.");
//         toast.error("Tasks could not be loaded.");
//       } finally {
//         setLoading(false);
//       }
//     };

//     loadTasks();
//   }, []);

//   return (
//     <main className="user-tasks-page">
//       <section className="user-tasks-page__header">
//         <div>
//           <p className="user-tasks-page__eyebrow">Workspace</p>

//           <h1>My Tasks</h1>

//           <p className="user-tasks-page__description">
//             View and manage the tasks you created.
//           </p>
//         </div>

//         <button
//           className="user-tasks-page__create-button"
//           onClick={() => navigate("/tasks/create-task")}
//         >
//           Create Task
//         </button>
//       </section>

//       {loading && (
//         <div className="user-tasks-page__state">Loading tasks...</div>
//       )}

//       {error && (
//         <div className="user-tasks-page__state user-tasks-page__state--error">
//           {error}
//         </div>
//       )}

//       {!loading && !error && tasks.length === 0 && (
//         <div className="user-tasks-page__empty">
//           <h2>No tasks yet</h2>

//           <p>You have not created any tasks yet.</p>
//         </div>
//       )}

//       {!loading && !error && tasks.length > 0 && (
//         <section className="user-tasks-page__grid">
//           {tasks.map((task) => (
//             <article key={task.id} className="user-tasks-page__card">
//               <div className="user-tasks-page__card-top">
//                 <span className="user-tasks-page__priority">
//                   {task.priority}
//                 </span>

//                 <span className="user-tasks-page__status">{task.status}</span>
//               </div>

//               <h2>{task.title}</h2>

//               <p>{task.description}</p>

//               <div className="user-tasks-page__meta">
//                 <span>{task.category}</span>

//                 {task.dueDate && <span>{task.dueDate}</span>}
//               </div>
//             </article>
//           ))}
//         </section>
//       )}
//     </main>
//   );
// }

// export default UserTasksPage;

import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import toast from "react-hot-toast";

import type { Task } from "../types/task";

type LocationState = {
  successMessage?: string;
};

function UserTasksPage() {
  const navigate = useNavigate();
  const location = useLocation();

  const hasShownSuccessToast = useRef(false);

  const [tasks, setTasks] = useState<Task[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    const state = location.state as LocationState | null;

    if (state?.successMessage && !hasShownSuccessToast.current) {
      hasShownSuccessToast.current = true;

      toast.success(state.successMessage);

      navigate(location.pathname, {
        replace: true,
        state: null,
      });
    }
  }, [location, navigate]);

  useEffect(() => {
    const loadTasks = async () => {
      try {
        setLoading(true);

        // Backend endpoint hazır olunca burada API isteği yapılacak.
        setTasks([]);
      } catch (err) {
        console.error(err);

        setError("Tasks could not be loaded.");
        toast.error("Tasks could not be loaded.");
      } finally {
        setLoading(false);
      }
    };

    loadTasks();
  }, []);

  return (
    <main className="user-tasks-page">
      <section className="user-tasks-page__header">
        <div>
          <p className="user-tasks-page__eyebrow">Workspace</p>

          <h1>My Tasks</h1>

          <p className="user-tasks-page__description">
            View and manage the tasks you created.
          </p>
        </div>

        <button
          className="user-tasks-page__create-button"
          onClick={() => navigate("/tasks/create-task")}
        >
          Create Task
        </button>
      </section>

      {loading && (
        <div className="user-tasks-page__state">Loading tasks...</div>
      )}

      {error && (
        <div className="user-tasks-page__state user-tasks-page__state--error">
          {error}
        </div>
      )}

      {!loading && !error && tasks.length === 0 && (
        <div className="user-tasks-page__empty">
          <h2>No tasks yet</h2>

          <p>You have not created any tasks yet.</p>
        </div>
      )}

      {!loading && !error && tasks.length > 0 && (
        <section className="user-tasks-page__grid">
          {tasks.map((task) => (
            <article key={task.id} className="user-tasks-page__card">
              <div className="user-tasks-page__card-top">
                <span className="user-tasks-page__priority">
                  {task.priority}
                </span>

                <span className="user-tasks-page__status">{task.status}</span>
              </div>

              <h2>{task.title}</h2>

              <p>{task.description}</p>

              <div className="user-tasks-page__meta">
                <span>{task.category}</span>

                {task.dueDate && <span>{task.dueDate}</span>}
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  );
}

export default UserTasksPage;