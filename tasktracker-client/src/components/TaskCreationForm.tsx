import { useRef, useState } from "react";
import type { ChangeEvent, FormEvent } from "react";
import type { TaskPriority } from "../types/CreateTaskRequest";
import type { TaskCreationFormValue } from "../types/taskCreationForm";

export type TaskCreationAssignee = {
  id: number;
  label: string;
};

type TaskCreationFormProps = {
  idPrefix: string;
  value: TaskCreationFormValue;
  onChange: (value: TaskCreationFormValue) => void;
  onSubmit: (value: TaskCreationFormValue) => void | Promise<void>;
  onCancel: () => void;
  submitting: boolean;
  error?: string;
  onClearError?: () => void;
  assignees?: TaskCreationAssignee[];
  heading?: string;
  description?: string;
  embedded?: boolean;
};

const priorities: TaskPriority[] = ["Low", "Medium", "High", "Critical"];

function utcToday() {
  return new Date().toISOString().slice(0, 10);
}

export default function TaskCreationForm({
  idPrefix,
  value,
  onChange,
  onSubmit,
  onCancel,
  submitting,
  error = "",
  onClearError,
  assignees,
  heading,
  description,
  embedded = false,
}: TaskCreationFormProps) {
  const [validationError, setValidationError] = useState("");
  const titleRef = useRef<HTMLInputElement>(null);
  const descriptionRef = useRef<HTMLTextAreaElement>(null);
  const categoryRef = useRef<HTMLInputElement>(null);
  const dueDateRef = useRef<HTMLInputElement>(null);

  const updateField = (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
    const { name, value: fieldValue } = event.target;
    onChange({
      ...value,
      [name]: name === "assigneeUserId"
        ? fieldValue ? Number(fieldValue) : null
        : name === "dueDate" ? fieldValue || null : fieldValue,
    });
    if (validationError) setValidationError("");
    if (error) onClearError?.();
  };

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalized: TaskCreationFormValue = {
      ...value,
      title: value.title.trim(),
      description: value.description.trim(),
      category: value.category.trim(),
    };

    if (normalized.title.length < 3) {
      setValidationError("Title must contain at least 3 characters.");
      titleRef.current?.focus();
      return;
    }
    if (normalized.description.length < 5) {
      setValidationError("Description must contain at least 5 characters.");
      descriptionRef.current?.focus();
      return;
    }
    if (!normalized.category) {
      setValidationError("Category is required.");
      categoryRef.current?.focus();
      return;
    }
    if (normalized.dueDate && normalized.dueDate < utcToday()) {
      setValidationError("Due date cannot be in the past.");
      dueDateRef.current?.focus();
      return;
    }

    setValidationError("");
    void onSubmit(normalized);
  };

  const displayedError = validationError || error;

  return (
    <form className={`task-creation-form${embedded ? " task-creation-form--embedded" : ""}`}
      onSubmit={handleSubmit} aria-busy={submitting} noValidate>
      {heading && (
        <div className="task-creation-form__header">
          <h3>{heading}</h3>
          {description && <p>{description}</p>}
        </div>
      )}

      <div className="task-creation-form__field">
        <label htmlFor={`${idPrefix}-title`}>Title <span aria-hidden="true">*</span></label>
        <input ref={titleRef} id={`${idPrefix}-title`} name="title" value={value.title} maxLength={100}
          minLength={3} onChange={updateField} disabled={submitting} required
          placeholder="What needs to be done?" />
      </div>

      <div className="task-creation-form__field">
        <label htmlFor={`${idPrefix}-description`}>Description <span aria-hidden="true">*</span></label>
        <textarea ref={descriptionRef} id={`${idPrefix}-description`} name="description" value={value.description}
          maxLength={1000} minLength={5} onChange={updateField} disabled={submitting}
          rows={4} required placeholder="Add the details someone needs to complete this task." />
      </div>

      <section className="task-creation-form__planning" aria-labelledby={`${idPrefix}-planning-title`}>
        <div className="task-creation-form__planning-heading">
          <h3 id={`${idPrefix}-planning-title`}>Planning details</h3>
          <p>Group the task, set its urgency, and establish a realistic deadline.</p>
        </div>

        <div className="task-creation-form__planning-grid">
          <div className="task-creation-form__field">
            <label htmlFor={`${idPrefix}-category`}>Category <span aria-hidden="true">*</span></label>
            <input ref={categoryRef} id={`${idPrefix}-category`} name="category" value={value.category}
              onChange={updateField} disabled={submitting} required
              placeholder="Example: Product, Operations, School" />
          </div>

          <div className="task-creation-form__field">
            <label htmlFor={`${idPrefix}-priority`}>Priority</label>
            <select id={`${idPrefix}-priority`} name="priority" value={value.priority}
              onChange={updateField} disabled={submitting}>
              {priorities.map((priority) => <option key={priority} value={priority}>{priority}</option>)}
            </select>
          </div>

          <div className="task-creation-form__field">
            <label htmlFor={`${idPrefix}-due-date`}>Due date <span className="task-creation-form__optional">Optional</span></label>
            <input ref={dueDateRef} id={`${idPrefix}-due-date`} name="dueDate" type="date"
              value={value.dueDate ?? ""} min={utcToday()} onChange={updateField} disabled={submitting} />
          </div>

          {assignees && (
            <div className="task-creation-form__field">
              <label htmlFor={`${idPrefix}-assignee`}>Assignee <span className="task-creation-form__optional">Optional</span></label>
              <select id={`${idPrefix}-assignee`} name="assigneeUserId"
                value={value.assigneeUserId ?? ""} onChange={updateField} disabled={submitting}>
                <option value="">Unassigned</option>
                {assignees.map((assignee) => (
                  <option key={assignee.id} value={assignee.id}>{assignee.label}</option>
                ))}
              </select>
            </div>
          )}
        </div>
      </section>

      {displayedError && <p className="task-creation-form__error" role="alert">{displayedError}</p>}

      <div className="task-creation-form__actions">
        <button className="secondary-button" disabled={submitting} onClick={onCancel} type="button">
          Cancel
        </button>
        <button className="primary-button" disabled={submitting} type="submit">
          {submitting ? "Creating..." : "Create task"}
        </button>
      </div>
    </form>
  );
}
