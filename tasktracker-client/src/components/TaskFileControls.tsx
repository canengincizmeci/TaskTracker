import { useId, useRef, useState } from "react";
import { deleteTaskAttachment, downloadTaskAttachment } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import type { TaskAttachment } from "../types/taskAttachment";
import "../styles/task-collaboration.css";

const accepted = ".pdf,.docx,.xlsx,.pptx,.txt,.csv,.png,.jpg,.jpeg,.webp,.zip";

function fileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.ceil(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const paths = {
  attach: "m8 13 6.2-6.2a2.2 2.2 0 0 1 3.1 3.1L9.7 17.5a4 4 0 1 1-5.6-5.7l8.1-8.1a5.5 5.5 0 1 1 7.8 7.8l-8.2 8.2",
  download: "M12 3v12m-5-5 5 5 5-5M4 15v5h16v-5",
  file: "M5 3h9l5 5v13H5zM14 3v6h5",
  discussion: "M20 15a2 2 0 0 1-2 2H9l-5 4V5a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2zM8 8h8M8 12h6",
  refresh: "M20 4v5h-5M4 20v-5h5M19.3 9a7.5 7.5 0 0 0-13-2M4.7 15a7.5 7.5 0 0 0 13 2",
  close: "m6 6 12 12m0-12L6 18",
  send: "m3 4 18 8-18 8 3-8zM6 12h15",
  trash: "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 10v7m4-7v7",
};

export function CollaborationIcon({ name }: { name: keyof typeof paths }) {
  return <svg className="tc-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d={paths[name]} />
  </svg>;
}

export function FilePicker({ file, onChange, label, disabled = false }: {
  file: File | null;
  onChange: (next: File | null) => void;
  label: string;
  disabled?: boolean;
}) {
  const id = useId();
  const [error, setError] = useState("");
  const [over, setOver] = useState(false);
  const depth = useRef(0);
  const pick = (selected: FileList | null) => {
    if (disabled || !selected?.length) return;
    const next = selected[0];
    const extension = next.name.slice(next.name.lastIndexOf(".")).toLowerCase();
    let issue = "";
    if (selected.length > 1) issue = "Choose one file at a time.";
    else if (!next.size) issue = "Choose a non-empty file.";
    else if (next.name.length > 255) issue = "Use a filename under 256 characters.";
    else if (!accepted.split(",").includes(extension)) issue = "Choose a PDF, Office document, text file, CSV, PNG, JPEG, WebP or ZIP.";
    setError(issue);
    onChange(issue ? null : next);
  };
  return <div className={`tc-picker${over ? " tc-picker--over" : ""}`}
    onDragEnter={(e) => {
      if (!e.dataTransfer.types.includes("Files")) return;
      e.preventDefault(); depth.current++; if (!disabled) setOver(true);
    }}
    onDragLeave={() => { depth.current = Math.max(0, depth.current - 1); if (!depth.current) setOver(false); }}
    onDragOver={(e) => {
      if (e.dataTransfer.types.includes("Files")) { e.preventDefault(); e.dataTransfer.dropEffect = disabled ? "none" : "copy"; }
    }}
    onDrop={(e) => {
      if (!e.dataTransfer.types.includes("Files")) return;
      e.preventDefault(); depth.current = 0; setOver(false); pick(e.dataTransfer.files);
    }}>
    <div className="tc-picker__row">
      <input type="file" id={id} className="tc-native-picker" accept={accepted} disabled={disabled}
        aria-invalid={!!error || undefined} aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => { pick(e.target.files); e.target.value = ""; }} />
      <label className="tc-action tc-picker__label" htmlFor={id}>
        <CollaborationIcon name="attach" /><span>{file ? "Change file" : label}</span>
      </label>
      {file && <span className="tc-selected">
        <span>{file.name} <small>· {fileSize(file.size)}</small></span>
        <button className="tc-icon-button" type="button" aria-label={`Clear ${file.name}`} disabled={disabled}
          onClick={() => { setError(""); onChange(null); }}><CollaborationIcon name="close" /></button>
      </span>}
    </div>
    {error && <p id={`${id}-error`} className="tc-error" role="alert">{error}</p>}
  </div>;
}

export function AttachmentChips({ files, detailed = false }: { files: TaskAttachment[]; detailed?: boolean }) {
  if (!files.length) return null;
  return <ul className={detailed ? "tc-file-index" : "tc-file-chips"}>
    {files.map((file) => <AttachmentItem key={file.id} file={file} detailed={detailed} />)}
  </ul>;
}

function AttachmentItem({ file, detailed }: { file: TaskAttachment; detailed: boolean }) {
  const [busy, setBusy] = useState<"download" | "delete" | null>(null);
  const [error, setError] = useState("");
  const [confirm, setConfirm] = useState(false);
  const guard = useRef(false);
  const removeButton = useRef<HTMLButtonElement>(null);
  const operate = async (action: "download" | "delete") => {
    if (guard.current) return;
    guard.current = true; setBusy(action); setError("");
    try {
      if (action === "download") await downloadTaskAttachment(file);
      else { await deleteTaskAttachment(file); setConfirm(false); }
    } catch (reason) {
      setError(errorMessage(reason, action === "download" ? "Could not download the file. Try again." : "Could not remove the file. Try again."));
    } finally {
      guard.current = false; setBusy(null);
    }
  };
  const label = file.fileName.split(".").at(-1)?.toUpperCase() ?? "FILE";
  return <li className={detailed ? "tc-file-row" : "tc-file-chip"}>
    <button className="tc-file-download" type="button" disabled={!!busy} aria-label={`Download ${file.fileName}`}
      onClick={() => void operate("download")}>
      <span className="tc-file-type" aria-hidden="true">{detailed ? label : <CollaborationIcon name="file" />}</span>
      <span className="tc-file-info"><strong>{file.fileName}</strong>
        <small>{detailed ? <>{fileSize(file.sizeBytes)} · {file.uploaderUserName} · <time dateTime={file.createdAt}>
          {new Date(file.createdAt).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}</time></> : fileSize(file.sizeBytes)}</small>
      </span>
      <span className="tc-download-icon">{busy === "download" ? "…" : <CollaborationIcon name="download" />}</span>
    </button>
    {detailed && file.canDelete && <div className="tc-file-remove">
      <button className="tc-icon-button" ref={removeButton} type="button" disabled={!!busy} aria-expanded={confirm}
        aria-label={`Remove ${file.fileName}`} title="Remove file" onClick={() => setConfirm((open) => !open)}>
        <CollaborationIcon name="trash" />
      </button>
    </div>}
    {confirm && <div className="tc-confirm" role="group" aria-label={`Confirm removal of ${file.fileName}`}>
      <span>Remove this reference file?</span>
      <button className="tc-action" type="button" disabled={!!busy}
        onClick={() => { setConfirm(false); removeButton.current?.focus(); }}>Keep file</button>
      <button className="tc-action tc-action--danger" type="button" disabled={!!busy}
        onClick={() => void operate("delete")}>{busy === "delete" ? "Removing…" : "Remove"}</button>
    </div>}
    {error && <p className="tc-error" role="alert">{error}</p>}
  </li>;
}
