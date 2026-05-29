import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";

import { createTask } from "../api/taskService";
import type { CreateTaskRequest } from "../types/CreateTaskRequest";
import toast from "react-hot-toast";

function CreateTaskPage() {
  const navigate = useNavigate();

  const [formData, setFormData] = useState<CreateTaskRequest>({
    title: "",
    description: "",
    category: "",
    priority: "Medium",
    status: "Pending",
    dueDate: null,
  });

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  const handleChange = (
    e: React.ChangeEvent<
      HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement
    >,
  ) => {
    const { name, value } = e.target;

    setFormData((prev: CreateTaskRequest) => ({
      ...prev,
      [name]: name === "dueDate" ? value || null : value,
    }));
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();

    setError("");
    setSuccessMessage("");

    if (!formData.title.trim()) {
      setError("Title is required.");
      return;
    }

    if (!formData.description.trim()) {
      setError("Description is required.");
      return;
    }

    if (!formData.category.trim()) {
      setError("Category is required.");
      return;
    }

    try {
      setLoading(true);

      // const result = await createTask(formData);

      // const message =
      //   typeof result === "string" ? result : "Task created successfully.";

      await createTask(formData);

      const message = "Task created successfully.";

      setSuccessMessage(message);
      // toast.success(message);

      navigate("/tasks/user-tasks", {
        state: {
          successMessage: message,
        },
      });
    } catch (err: any) {
      console.error("Create task error:", err);

      const apiError = err?.response?.data;

      const message =
        typeof apiError === "string"
          ? apiError
          : apiError?.title
            ? apiError.title
            : "Task could not be created.";

      setError(message);
      toast.error(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <main className="create-task-page">
      <section className="create-task-page__card">
        <div className="create-task-page__header">
          <p className="create-task-page__eyebrow">Task Flow</p>

          <h1>Create a new task</h1>

          <p>
            Define your task, choose a priority level and keep your workflow
            organized.
          </p>
        </div>

        {error && (
          <div className="create-task-page__alert create-task-page__alert--error">
            {error}
          </div>
        )}

        {successMessage && (
          <div className="create-task-page__alert create-task-page__alert--success">
            {successMessage}
          </div>
        )}

        <form className="create-task-page__form" onSubmit={handleSubmit}>
          <div className="create-task-page__field">
            <label htmlFor="title">Title</label>

            <input
              id="title"
              name="title"
              type="text"
              value={formData.title}
              onChange={handleChange}
              placeholder="Example: Prepare project report"
            />
          </div>

          <div className="create-task-page__field">
            <label htmlFor="description">Description</label>

            <textarea
              id="description"
              name="description"
              value={formData.description}
              onChange={handleChange}
              placeholder="Describe the task details..."
              rows={5}
            />
          </div>

          <div className="create-task-page__grid">
            <div className="create-task-page__field">
              <label htmlFor="category">Category</label>

              <input
                id="category"
                name="category"
                type="text"
                value={formData.category}
                onChange={handleChange}
                placeholder="Development, School..."
              />
            </div>

            <div className="create-task-page__field">
              <label htmlFor="priority">Priority</label>

              <select
                id="priority"
                name="priority"
                value={formData.priority}
                onChange={handleChange}
              >
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
          </div>

          <div className="create-task-page__field">
            <label htmlFor="dueDate">Due Date</label>

            <input
              id="dueDate"
              name="dueDate"
              type="date"
              value={formData.dueDate ?? ""}
              onChange={handleChange}
            />
          </div>

          <div className="create-task-page__actions">
            <button
              type="button"
              className="create-task-page__button create-task-page__button--secondary"
              onClick={() => navigate(-1)}
            >
              Cancel
            </button>

            <button
              type="submit"
              disabled={loading}
              className="create-task-page__button create-task-page__button--primary"
            >
              {loading ? "Creating..." : "Create Task"}
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}

export default CreateTaskPage;
