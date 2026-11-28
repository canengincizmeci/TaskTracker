import { useRef, useState, type FormEvent, type RefObject } from "react";
import { isAxiosError } from "axios";
import { updateTaskMlConsent } from "../api/taskService";
import type { Task } from "../types/task";
import MlConsentCheckbox from "./MlConsentCheckbox";
import { TaskDialog } from "./TaskEditDialog";

export default function TaskMlConsentDialog({ task, onClose, onSaved, refreshTask, opener }: {
  task: Task;
  onClose: () => void;
  onSaved: () => Promise<void>;
  refreshTask: () => Promise<number | undefined>;
  opener: RefObject<HTMLElement | null>;
}) {
  const [initial] = useState(() => ({ granted: task.mlConsent?.granted === true, version: task.version }));
  const [granted, setGranted] = useState(initial.granted);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [stale, setStale] = useState(false);
  const sending = useRef(false);
  const changed = stale || task.version !== initial.version;

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current || changed || granted === initial.granted) return;
    sending.current = true;
    setBusy(true);
    setError("");
    try {
      await updateTaskMlConsent(task.id, granted, initial.version);
      await onSaved();
    } catch (reason) {
      if (isAxiosError(reason) && reason.response?.status === 409) {
        setStale(true);
        await refreshTask().catch(() => undefined);
      } else {
        setError("Veri kullanım izni kaydedilemedi. Görev durumunu kontrol edip tekrar deneyin.");
      }
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <TaskDialog title="Makine öğrenmesi veri kullanım izni" onClose={onClose} opener={opener} busy={busy}>
    <form onSubmit={save} lang="tr">
      <MlConsentCheckbox checked={granted} disabled={busy || changed} onChange={setGranted} />
      <p>Daha önce indirilmiş veri setleri ve eğitilmiş modeller otomatik olarak güncellenmez.</p>
      {(error || changed) && <p className="tw-notice tw-notice--error" role="alert">
        {changed ? "Görev değişti. Güncel izni görmek için bu pencereyi kapatıp yeniden açın." : error}
      </p>}
      <div className="tw-dialog-actions">
        <button className="tw-control" type="button" disabled={busy} onClick={onClose}>Vazgeç</button>
        <button className="tw-control tw-control--primary" type="submit"
          disabled={busy || changed || granted === initial.granted}>{busy ? "Kaydediliyor…" : "İzni kaydet"}</button>
      </div>
    </form>
  </TaskDialog>;
}
