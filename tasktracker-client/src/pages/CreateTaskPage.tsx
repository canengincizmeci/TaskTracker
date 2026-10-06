import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import toast from "react-hot-toast";

import { errorMessage } from "../api/errorMessage";
import { createTask } from "../api/taskService";
import TaskCreationForm from "../components/TaskCreationForm";
import type { CreateTaskRequest } from "../types/CreateTaskRequest";
import {
  createEmptyTaskCreationValue,
  type TaskCreationFormValue,
} from "../types/taskCreationForm";

function CreateTaskPage() {
  const navigate = useNavigate();
  const [draft, setDraft] = useState<TaskCreationFormValue>(createEmptyTaskCreationValue);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const requestId = useRef(0);
  const createInProgress = useRef(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const handleSubmit = async (value: TaskCreationFormValue) => {
    if (createInProgress.current) return;
    const currentRequest = ++requestId.current;
    const isCurrent = () => mounted.current && requestId.current === currentRequest;
    const request: CreateTaskRequest = {
      title: value.title,
      description: value.description,
      category: value.category,
      priority: value.priority,
      dueDate: value.dueDate,
    };

    createInProgress.current = true;
    setSubmitting(true);
    setError("");

    try {
      await createTask(request);
      if (!isCurrent()) return;
      navigate("/dashboard?scope=owned", {
        state: { successMessage: "Task created successfully." },
      });
    } catch (reason: unknown) {
      if (!isCurrent()) return;
      const message = errorMessage(reason, "Task could not be created.");
      setError(message);
      toast.error(message);
    } finally {
      if (isCurrent()) {
        createInProgress.current = false;
        setSubmitting(false);
      }
    }
  };

  return (
    <main className="create-task-page">
      <section className="create-task-page__card">
        <div className="create-task-page__header">
          <p className="create-task-page__eyebrow">My Work</p>
          <h1>Create a task</h1>
          <p>Capture the essentials now. You can add workflow details after creation.</p>
        </div>

        <TaskCreationForm
          idPrefix="personal-task"
          value={draft}
          onChange={setDraft}
          onSubmit={handleSubmit}
          onCancel={() => navigate(-1)}
          submitting={submitting}
          error={error}
          onClearError={() => setError("")}
        />
      </section>
    </main>
  );
}

export default CreateTaskPage;
