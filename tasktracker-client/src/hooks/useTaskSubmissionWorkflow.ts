import { useCallback, useEffect, useRef, useState } from "react";
import { getTaskSubmissions, reviewSubmission, submitTask } from "../api/taskService";
import { errorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import type { TaskSubmission } from "../types/taskSubmission";

export type TaskSubmissionWorkflow = {
  submissions: TaskSubmission[];
  latest: TaskSubmission | undefined;
  loading: boolean;
  busy: boolean;
  failure: string;
  historyLoadFailed: boolean;
  content: string;
  feedback: string;
  setContent: (value: string) => void;
  setFeedback: (value: string) => void;
  submit: () => Promise<void>;
  review: (decision: "Approved" | "ChangesRequested") => Promise<void>;
};

export function useTaskSubmissionWorkflow(task: Task, onChanged: () => Promise<void>): TaskSubmissionWorkflow {
  const [submissions, setSubmissions] = useState<TaskSubmission[]>([]);
  const [content, setContent] = useState("");
  const [feedback, setFeedback] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [historyFailure, setHistoryFailure] = useState("");
  const [actionFailure, setActionFailure] = useState("");
  const mounted = useRef(true);
  const currentTaskId = useRef(task.id);
  const taskGeneration = useRef(0);
  const historyGeneration = useRef(0);

  const isCurrent = useCallback((taskId: number, requestGeneration: number) =>
    mounted.current && currentTaskId.current === taskId && taskGeneration.current === requestGeneration, []);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      taskGeneration.current += 1;
      historyGeneration.current += 1;
    };
  }, []);

  useEffect(() => {
    if (currentTaskId.current === task.id) return;
    currentTaskId.current = task.id;
    taskGeneration.current += 1;
    historyGeneration.current += 1;
  }, [task.id]);

  const load = useCallback(async () => {
    const taskId = task.id;
    const requestGeneration = taskGeneration.current;
    const historyRequest = ++historyGeneration.current;
    const canApply = () => isCurrent(taskId, requestGeneration) && historyGeneration.current === historyRequest;
    try {
      const history = await getTaskSubmissions(taskId);
      if (!canApply()) return;
      setSubmissions(history);
      setHistoryFailure("");
    } catch (reason) {
      if (!canApply()) return;
      setHistoryFailure(errorMessage(reason, "Submitted work history could not be loaded."));
    } finally {
      if (canApply()) setLoading(false);
    }
  }, [isCurrent, task.id]);

  useEffect(() => {
    const initial = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(initial);
  }, [load, task.version]);

  useEffect(() => {
    const refresh = () => { void load(); };
    window.addEventListener("focus", refresh);
    return () => window.removeEventListener("focus", refresh);
  }, [load]);

  const latest = submissions.at(-1);
  const refresh = async (taskId: number, requestGeneration: number) => {
    if (!isCurrent(taskId, requestGeneration)) return;
    await Promise.allSettled([load(), onChanged()]);
  };

  const submit = async () => {
    if (busy || !content.trim()) return;
    const taskId = task.id;
    const taskVersion = task.version;
    const requestGeneration = taskGeneration.current;
    setBusy(true); setActionFailure("");
    try {
      await submitTask(taskId, taskVersion, content);
      if (!isCurrent(taskId, requestGeneration)) return;
      setContent("");
      await refresh(taskId, requestGeneration);
    } catch (reason) {
      if (!isCurrent(taskId, requestGeneration)) return;
      setActionFailure(errorMessage(reason, "Work could not be submitted. Your text has been kept."));
      await refresh(taskId, requestGeneration);
    } finally {
      if (isCurrent(taskId, requestGeneration)) setBusy(false);
    }
  };

  const review = async (decision: "Approved" | "ChangesRequested") => {
    if (busy || !latest || (decision === "ChangesRequested" && !feedback.trim())) return;
    const taskId = task.id;
    const taskVersion = task.version;
    const submissionId = latest.id;
    const requestGeneration = taskGeneration.current;
    setBusy(true); setActionFailure("");
    try {
      await reviewSubmission(taskId, submissionId, taskVersion, decision, feedback);
      if (!isCurrent(taskId, requestGeneration)) return;
      setFeedback("");
      await refresh(taskId, requestGeneration);
    } catch (reason) {
      if (!isCurrent(taskId, requestGeneration)) return;
      setActionFailure(errorMessage(reason, "The review could not be saved."));
      await refresh(taskId, requestGeneration);
    } finally {
      if (isCurrent(taskId, requestGeneration)) setBusy(false);
    }
  };

  return { submissions, latest, loading, busy, failure: historyFailure || actionFailure,
    historyLoadFailed: Boolean(historyFailure), content, feedback, setContent, setFeedback, submit, review };
}
