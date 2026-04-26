import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { createTask, deleteTask, getAllTasks } from "../api/taskService";
import type { Task } from "../types/task";
import TaskCard from "../components/TaskCard";

function AdminDashboardPage() {
  const navigate = useNavigate();

  const [tasks, setTasks] = useState<Task[]>([]);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState("");
  const [priority, setPriority] = useState("Medium");
  const [status, setStatus] = useState("Open");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const loadTasks = async () => {
    const data = await getAllTasks();
    setTasks(data);
  };

  useEffect(() => {
    const token = sessionStorage.getItem("adminToken");
    const expireAt = sessionStorage.getItem("adminTokenExpireAt");

    if (!token || !expireAt) {
      navigate("/secret-admin-entry");
      return;
    }

    const expireDate = new Date(expireAt);

    if (expireDate <= new Date()) {
      sessionStorage.removeItem("adminToken");
      sessionStorage.removeItem("adminTokenExpireAt");
      navigate("/secret-admin-entry");
      return;
    }

    loadTasks();
  }, [navigate]);

  const handleCreateTask = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    try {
      setLoading(true);
      setError("");

      await createTask({
        title,
        description,
        category,
        priority,
        status,
      });

      setTitle("");
      setDescription("");
      setCategory("");
      setPriority("Medium");
      setStatus("Open");

      await loadTasks();
    } catch (error) {
      console.log(error);
      setError("Task could not be created.");
    } finally {
      setLoading(false);
    }
  };

  const handleDeleteTask = async (id: number) => {
    const confirmed = window.confirm("Are you sure you want to delete this task?");

    if (!confirmed) return;

    try {
      setError("");
      await deleteTask(id);
      await loadTasks();
    } catch (error) {
      console.log(error);
      setError("Task could not be deleted.");
    }
  };

  const handleLogout = () => {
    sessionStorage.removeItem("adminToken");
    sessionStorage.removeItem("adminTokenExpireAt");
    navigate("/");
  };

  return (
    <main className="page">
      <section className="admin-header">
        <div>
          <p className="eyebrow">ADMIN DASHBOARD</p>
          <h1>Manage Tasks</h1>
          <p className="hero-text">
            Create and remove task requests from this protected admin panel.
          </p>
        </div>

        <button className="secondary-button" onClick={handleLogout}>
          Logout
        </button>
      </section>

      <section className="admin-layout">
        <form className="admin-form" onSubmit={handleCreateTask}>
          <h2>Create Task</h2>

          <div className="form-group">
            <label>Title</label>
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
            />
          </div>

          <div className="form-group">
            <label>Description</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={5}
              required
            />
          </div>

          <div className="form-group">
            <label>Category</label>
            <input
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              required
            />
          </div>

          <div className="form-group">
            <label>Priority</label>
            <select
              value={priority}
              onChange={(e) => setPriority(e.target.value)}
            >
              <option>Low</option>
              <option>Medium</option>
              <option>High</option>
              <option>Critical</option>
            </select>
          </div>

          <div className="form-group">
            <label>Status</label>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option>Open</option>
              <option>In Progress</option>
              <option>Done</option>
            </select>
          </div>

          {error && <p className="error-message">{error}</p>}

          <button className="primary-button" type="submit" disabled={loading}>
            {loading ? "Creating..." : "Create Task"}
          </button>
        </form>

        <section className="admin-task-list">
          {tasks.map((task) => (
            <div key={task.id} className="admin-task-item">
              <TaskCard task={task} />

              <button
                className="danger-button"
                onClick={() => handleDeleteTask(task.id)}
              >
                Delete
              </button>
            </div>
          ))}
        </section>
      </section>
    </main>
  );
}

export default AdminDashboardPage;