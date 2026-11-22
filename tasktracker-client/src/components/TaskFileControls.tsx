import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from "react";
import { deleteTaskAttachment, downloadTaskAttachment, getTaskAttachmentBlob } from "../api/taskAttachmentService";
import { errorMessage } from "../api/errorMessage";
import type { TaskAttachment } from "../types/taskAttachment";
import type { DiscussionAttachmentSettings } from "../types/taskCollaboration";
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

function fileIssue(file: File) {
  const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
  if (!file.size) return "Choose a non-empty file.";
  if (file.name.length > 255) return "Use a filename under 256 characters.";
  if (!accepted.split(",").includes(extension)) return "Choose a PDF, Office document, text file, CSV, PNG, JPEG, WebP or ZIP.";
  return "";
}

function FileInput({ label, disabled, multiple, onFiles, picker, error, children }: {
  label: string; disabled: boolean; multiple?: boolean; onFiles: (selected: File[]) => void;
  picker?: RefObject<HTMLInputElement | null>; error: string; children?: ReactNode;
}) {
  const id = useId();
  const [over, setOver] = useState(false);
  const depth = useRef(0);
  const pick = (selected: FileList | null) => {
    if (disabled || !selected?.length) return;
    onFiles(Array.from(selected));
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
      <input ref={picker} type="file" id={id} className="tc-native-picker" accept={accepted} disabled={disabled} multiple={multiple}
        aria-invalid={!!error || undefined} aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => { pick(e.target.files); e.target.value = ""; }} />
      <label className="tc-action tc-picker__label" htmlFor={id}>
        <CollaborationIcon name="attach" /><span>{label}</span>
      </label>
      {children}
    </div>
    {error && <p id={`${id}-error`} className="tc-error" role="alert">{error}</p>}
  </div>;
}

export function FilePicker({ file, onChange, label, disabled = false }: {
  file: File | null; onChange: (next: File | null) => void; label: string; disabled?: boolean;
}) {
  const [error, setError] = useState("");
  return <FileInput label={file ? "Change file" : label} disabled={disabled} error={error} onFiles={selected => {
    const issue = selected.length > 1 ? "Choose one file at a time." : fileIssue(selected[0]);
    setError(issue);
    onChange(issue ? null : selected[0]);
  }}>
    {file && <span className="tc-selected">
        <span>{file.name} <small>· {fileSize(file.size)}</small></span>
        <button className="tc-icon-button" type="button" aria-label={`Clear ${file.name}`} disabled={disabled}
          onClick={() => { setError(""); onChange(null); }}><CollaborationIcon name="close" /></button>
      </span>}
  </FileInput>;
}

const imageTypes = new Set(["image/png", "image/jpeg", "image/webp"]);
const maxPreviewBytes = 4 * 1024 * 1024;
export type SelectedDiscussionFile = { id: number; file: File };

export function DiscussionFilePicker({ files, onChange, settings, disabled }: {
  files: SelectedDiscussionFile[]; onChange: (files: SelectedDiscussionFile[]) => void;
  settings: DiscussionAttachmentSettings; disabled: boolean;
}) {
  const [error, setError] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const selectedList = useRef<HTMLUListElement>(null);
  const nextId = useRef(0);

  function add(selected: File[]) {
    if (files.length + selected.length > settings.maxFiles) { setError(`Choose at most ${settings.maxFiles} files per message.`); return; }
    for (const file of selected) {
      const issue = fileIssue(file) || (file.size > settings.maxFileSizeBytes
        ? `The file exceeds the ${fileSize(settings.maxFileSizeBytes)} limit.` : "");
      if (issue) { setError(`${file.name}. ${issue}`); return; }
    }
    const total = files.reduce((size, item) => size + item.file.size, 0) + selected.reduce((size, file) => size + file.size, 0);
    if (total > settings.maxTotalBytes) {
      setError(`Selected files exceed the ${fileSize(settings.maxTotalBytes)} total limit.`); return;
    }
    setError("");
    onChange([...files, ...selected.map(file => ({ id: nextId.current++, file }))]);
  }

  function remove(index: number) {
    const buttons = selectedList.current?.querySelectorAll("button");
    (buttons?.[index + 1] ?? buttons?.[index - 1] ?? input.current)?.focus();
    setError("");
    onChange(files.filter((_, current) => current !== index));
  }

  return <div className="tc-discussion-picker">
    <FileInput label={files.length ? "Add files" : "Attach files"} picker={input} disabled={disabled || !settings.uploadsEnabled}
      multiple onFiles={add} error={error} />
    {files.length > 0 && <ul className="tc-selected-files" ref={selectedList} aria-label="Selected attachments">
      {files.map(({ id, file }, index) => <li key={id} className="tc-selected">
        {imageTypes.has(file.type) && file.size <= maxPreviewBytes && <SelectedImage file={file} />}
        <span>{file.name} <small>· {fileSize(file.size)}</small></span>
        <button className="tc-icon-button" type="button" aria-label={`Remove ${file.name}`} disabled={disabled} onClick={() => remove(index)}>
          <CollaborationIcon name="close" />
        </button>
      </li>)}
    </ul>}
    <small className="tc-muted">Up to {settings.maxFiles} files, {fileSize(settings.maxFileSizeBytes)} each and {fileSize(settings.maxTotalBytes)} total.</small>
  </div>;
}

function SelectedImage({ file }: { file: File }) {
  const image = useRef<HTMLImageElement>(null);
  useEffect(() => {
    if (typeof URL.createObjectURL !== "function") return;
    const target = image.current;
    if (!target) return;
    const source = URL.createObjectURL(file);
    target.src = source;
    return () => URL.revokeObjectURL(source);
  }, [file]);
  return <img ref={image} className="tc-selected-preview" alt="" onError={event => { event.currentTarget.hidden = true; }} />;
}

function DiscussionImage({ file }: { file: TaskAttachment }) {
  const { id, contentType } = file;
  const target = useRef<HTMLDivElement>(null);
  const [preview, setPreview] = useState<{ id: number; url: string }>();
  useEffect(() => {
    const host = target.current;
    if (!host || typeof URL.createObjectURL !== "function") return;
    const request = new AbortController();
    let url: string | undefined;
    const fetchPreview = () => {
      void getTaskAttachmentBlob(id, request.signal).then(blob => {
        if (request.signal.aborted) return;
        url = URL.createObjectURL(new Blob([blob], { type: contentType }));
        setPreview({ id, url });
      }).catch(() => undefined);
    };
    const observer = typeof IntersectionObserver === "function" ? new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) { observer?.disconnect(); fetchPreview(); }
    }) : null;
    if (observer) observer.observe(host); else fetchPreview();
    return () => { request.abort(); observer?.disconnect(); if (url) URL.revokeObjectURL(url); };
  }, [id, contentType]);

  return <div className="tc-message-image" ref={target}>{preview?.id === file.id &&
    <img src={preview.url} alt={`Preview of ${file.fileName}`} onError={event => { event.currentTarget.hidden = true; }} />}</div>;
}

export function AttachmentChips({ files, detailed = false, download = downloadTaskAttachment, previewImages = false }: {
  files: TaskAttachment[]; detailed?: boolean; download?: (file: TaskAttachment) => Promise<void>; previewImages?: boolean;
}) {
  if (!files.length) return null;
  return <ul className={detailed ? "tc-file-index" : "tc-file-chips"}>
    {files.map((file) => <AttachmentItem key={file.id} file={file} detailed={detailed} download={download} previewImages={previewImages} />)}
  </ul>;
}

function AttachmentItem({ file, detailed, download, previewImages }: {
  file: TaskAttachment; detailed: boolean; download: (file: TaskAttachment) => Promise<void>; previewImages: boolean;
}) {
  const [busy, setBusy] = useState<"download" | "delete" | null>(null);
  const [error, setError] = useState("");
  const [confirm, setConfirm] = useState(false);
  const guard = useRef(false);
  const removeButton = useRef<HTMLButtonElement>(null);
  const operate = async (action: "download" | "delete") => {
    if (guard.current) return;
    guard.current = true; setBusy(action); setError("");
    try {
      if (action === "download") await download(file);
      else { await deleteTaskAttachment(file); setConfirm(false); }
    } catch (reason) {
      setError(errorMessage(reason, action === "download" ? "Could not download the file. Try again." : "Could not remove the file. Try again."));
    } finally {
      guard.current = false; setBusy(null);
    }
  };
  const label = file.fileName.split(".").at(-1)?.toUpperCase() ?? "FILE";
  return <li className={detailed ? "tc-file-row" : "tc-file-chip"}>
    {previewImages && file.sizeBytes <= maxPreviewBytes && imageTypes.has(file.contentType) && <DiscussionImage file={file} />}
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
