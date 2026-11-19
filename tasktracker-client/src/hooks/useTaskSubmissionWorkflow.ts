import { useCallback, useEffect, useRef, useState } from "react";
import { isAxiosError } from "axios";
import { getTaskSubmissions, reviewSubmission, submitTask } from "../api/taskService";
import { uploadSubmissionAttachment } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import type { Task } from "../types/task";
import type { SubmissionReview, TaskSubmission } from "../types/taskSubmission";
import { deliveryFileIds, matchingDeliveryFileIds, sameFiles, workflowSubmission } from "../utils/taskWorkflowState";
import { useTaskAttachments } from "./useTaskAttachments";
import { useTaskConversation } from "./useTaskConversation";

type Operation = "submit" | "approve" | "requestChanges" | "attach";
type History = { version: number; items: TaskSubmission[]; error: string };
type Notice = { version: number; message: string };
type PendingUpload = { submission: TaskSubmission; file: File | null; reason: string; previousMatches?: number[] };
type ReviewSnapshot = { id: number; version: number; files: number[] };

function outcomeUncertain(reason: unknown) {
  return isAxiosError(reason) && (!reason.response || reason.response.status >= 500);
}

export type TaskSubmissionWorkflow = ReturnType<typeof useTaskSubmissionWorkflow>;

// The task/session boundary mounts a fresh instance. Drafts stay here while tabs change.
export function useTaskSubmissionWorkflow(task: Task, onChanged: () => Promise<number | undefined>,
  onActionStart?: () => void) {
  const attachments = useTaskAttachments();
  const { onAccessRevoked } = useTaskConversation();
  const [history, setHistory] = useState<History>();
  const [operation, setOperation] = useState<Operation | null>(null);
  const [actionFailure, setActionFailure] = useState("");
  const [reviewFileNotice, setReviewFileNotice] = useState<{ id: number; message: string }>();
  const [success, setSuccess] = useState<Notice>();
  const [needsRefresh, setNeedsRefresh] = useState<number>();
  const [pendingUpload, setPendingUpload] = useState<PendingUpload>();
  const [deliveryDraft, setDeliveryDraft] = useState({ scope: "", content: "", file: null as File | null });
  const [reviewDraft, setReviewDraft] = useState({ revisionId: 0, value: "" });
  const writing = useRef(false);
  const mounted = useRef(false);
  const request = useRef(0);
  const historyRead = useRef<{ version: number; response: Promise<void> } | undefined>(undefined);
  const canRead = task.canViewParticipants === true;
  const items = canRead ? history?.items ?? [] : [];
  const current = canRead && history?.version === task.version ? history : undefined;
  const latest = workflowSubmission(task.workflowSubmissionId, items);
  const draftScope = task.canSubmit ? `${task.assigneeUserId}:${task.workflowSubmissionId ?? "initial"}` : "";
  const content = deliveryDraft.scope === draftScope ? deliveryDraft.content : "";
  const file = deliveryDraft.scope === draftScope ? deliveryDraft.file : null;
  const feedback = latest && reviewDraft.revisionId === latest.id ? reviewDraft.value : "";

  const loadHistory = useCallback(() => {
    if (!canRead) return Promise.resolve();
    if (historyRead.current?.version === task.version) return historyRead.current.response;
    const sequence = ++request.current;
    const response = getTaskSubmissions(task.id).then(items => {
      if (mounted.current && sequence === request.current) setHistory({ version: task.version, items, error: "" });
    }, (reason: unknown) => {
      if (mounted.current && sequence === request.current && isAxiosError(reason) && reason.response?.status === 403) {
        onAccessRevoked();
        return;
      }
      if (mounted.current && sequence === request.current) setHistory(previous => ({
        version: task.version, items: previous?.items ?? [], error: errorMessage(reason, "Could not load submissions."),
      }));
    }).finally(() => {
      if (historyRead.current?.response === response) historyRead.current = undefined;
    });
    historyRead.current = { version: task.version, response };
    return response;
  }, [canRead, onAccessRevoked, task.id, task.version]);

  useEffect(() => {
    mounted.current = true;
    const initial = window.setTimeout(() => { void loadHistory(); }, 0);
    window.addEventListener("focus", loadHistory);
    return () => {
      mounted.current = false;
      request.current += 1;
      historyRead.current = undefined;
      window.clearTimeout(initial);
      window.removeEventListener("focus", loadHistory);
    };
  }, [loadHistory]);

  useEffect(() => {
    if (!draftScope) {
      const clear = window.setTimeout(() => setDeliveryDraft({ scope: "", content: "", file: null }), 0);
      return () => window.clearTimeout(clear);
    }
  }, [draftScope]);

  const sync = async () => {
    try {
      const version = await onChanged();
      if (mounted.current && version !== undefined) {
        if (version === task.version) await loadHistory();
        setNeedsRefresh(undefined);
      } else if (mounted.current) setNeedsRefresh(task.version);
      return version;
    } catch {
      if (mounted.current) setNeedsRefresh(task.version);
      return undefined;
    }
  };

  const begin = (action: Operation) => {
    if (writing.current || needsRefresh === task.version) return false;
    writing.current = true;
    setOperation(action);
    setActionFailure("");
    setSuccess(undefined);
    onActionStart?.();
    return true;
  };
  const finish = () => {
    writing.current = false;
    if (mounted.current) setOperation(null);
  };
  const failedWrite = async (reason: unknown, fallback: string) => {
    if (!mounted.current) return;
    setActionFailure(isAxiosError(reason) && reason.response?.status === 409
      ? "The task changed while you were working. Check the latest state before trying again."
      : errorMessage(reason, fallback));
    await sync();
  };
  const confirmWrite = async (message: string) => {
    if (!mounted.current) return;
    const version = await sync();
    if (mounted.current) setSuccess({ version: version ?? task.version, message });
  };

  const submit = async () => {
    if (!task.canSubmit || !content.trim() || !begin("submit")) return;
    try {
      const submission = await submitTask(task.id, task.version, content.trim());
      if (!mounted.current) return;
      setDeliveryDraft({ scope: "", content: "", file: null });
      setPendingUpload(undefined);
      if (file) {
        setOperation("attach");
        try {
          await uploadSubmissionAttachment(task.id, submission.id, file);
        } catch (reason) {
          if (mounted.current) setPendingUpload({ submission, file,
            reason: errorMessage(reason, "The upload could not be confirmed."),
            previousMatches: outcomeUncertain(reason) ? [] : undefined });
        }
      }
      await confirmWrite(`Revision ${submission.revisionNumber} was sent for review.`);
    } catch (reason) {
      await failedWrite(reason, "Could not confirm the submission. Check the latest revision before retrying.");
    } finally { finish(); }
  };

  const reviewFilesReady = Boolean(current && !current.error && !attachments.loading && !attachments.error);
  const reviewSnapshot = (): ReviewSnapshot | undefined => latest && reviewFilesReady
    ? { id: latest.id, version: task.version, files: deliveryFileIds(attachments.files, latest.id) } : undefined;
  const review = async (decision: SubmissionReview["decision"], expected = reviewSnapshot()) => {
    if (!task.canReview || !latest || latest.review || !expected || !reviewFilesReady ||
        (decision === "ChangesRequested" && !feedback.trim())) return;
    if (!begin(decision === "Approved" ? "approve" : "requestChanges")) return;
    try {
      setReviewFileNotice(undefined);
      let currentFiles: number[];
      try {
        currentFiles = deliveryFileIds(await attachments.refreshFresh(), latest.id);
      } catch {
        return; // The shared collection exposes its error and retry control on the delivery.
      }
      if (!mounted.current) return;
      if (!sameFiles(expected.files, currentFiles)) {
        setReviewFileNotice({ id: latest.id, message: "The delivery files changed. Check the updated files before sending your decision." });
        return;
      }
      if (expected.id !== latest.id || expected.version !== task.version) {
        setReviewFileNotice({ id: latest.id, message: "The task changed. Check the latest delivery before confirming your decision again." });
        return;
      }
      await reviewSubmission(task.id, latest.id, expected.version, decision, feedback.trim());
      if (mounted.current) setReviewDraft({ revisionId: 0, value: "" });
      await confirmWrite(decision === "Approved" ? "Revision approved. The task is complete."
        : "Changes requested. The task is back in progress.");
    } catch (reason) {
      await failedWrite(reason, "Could not confirm the decision. Check the latest review before retrying.");
    } finally { finish(); }
  };

  const canAttach = Boolean(task.isAssignee && task.status === "InReview" && latest && !latest.review &&
    latest.submittedByUserId === task.assigneeUserId && current && !current.error);
  const attachToRevision = async (submissionId: number, retryFile: File) => {
    if (!latest || !canAttach || latest.id !== submissionId || !begin("attach")) return;
    let previousMatches = pendingUpload?.submission.id === latest.id && pendingUpload.file === retryFile
      ? pendingUpload.previousMatches : undefined;
    try {
      const existing = await attachments.refreshFresh();
      if (!mounted.current) return;
      const matching = matchingDeliveryFileIds(existing, latest.id, latest.submittedByUserId, retryFile.name, retryFile.size);
      const originalMatches = previousMatches;
      const mayBeSaved = originalMatches && matching.some(id => !originalMatches.includes(id));
      previousMatches = matching;
      if (!mayBeSaved) {
        await uploadSubmissionAttachment(task.id, latest.id, retryFile);
      }
      if (!mounted.current) return;
      setPendingUpload(undefined);
      await confirmWrite(mayBeSaved ? "A new file with this name and size is on this revision. Open it to check the contents."
        : `File attached to revision ${latest.revisionNumber}.`);
    } catch (reason) {
      if (mounted.current) setPendingUpload({ submission: latest, file: retryFile,
        reason: errorMessage(reason, "Could not confirm the file upload. Check the connection and try again."),
        previousMatches: outcomeUncertain(reason) ? previousMatches : undefined });
      await sync();
    } finally { finish(); }
  };

  return {
    submissions: items, latest, loading: canRead && !current, historyError: current?.error ?? "",
    reloadHistory: loadHistory, sync, needsRefresh: needsRefresh === task.version,
    busy: operation !== null, operation, failure: actionFailure, success: success?.version === task.version ? success.message : "",
    content, setContent: (value: string) => setDeliveryDraft({ scope: draftScope, content: value, file }),
    feedback, setFeedback: (value: string) => setReviewDraft({ revisionId: latest?.id ?? 0, value }),
    file, setFile: (value: File | null) => setDeliveryDraft({ scope: draftScope, content, file: value }),
    submit, review, reviewSnapshot, pendingUpload: canRead ? pendingUpload : undefined,
    canRetryAttachment: canAttach && latest?.id === pendingUpload?.submission.id, canAttach, attachToRevision,
    reviewFilesReady,
    reviewFileNotice: reviewFileNotice && reviewFileNotice.id === latest?.id ? reviewFileNotice.message : "",
    dismissUpload: () => setPendingUpload(undefined),
    setRetryFile: (next: File | null) => setPendingUpload(currentUpload => currentUpload &&
      { ...currentUpload, file: next, previousMatches: undefined }),
  };
}
