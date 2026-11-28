import assert from "node:assert/strict";
import { after, afterEach, before, test } from "node:test";
import { fileURLToPath } from "node:url";
import { act, createElement as h, useState, useCallback } from "react";
import { JSDOM } from "jsdom";
import { HubConnectionBuilder } from "@microsoft/signalr";
import { createServer } from "vite";

const originalHubBuild = HubConnectionBuilder.prototype.build;
const objectUrls = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
const originalBase = process.env.VITE_API_BASE_URL;
const originals = new Map();
const roots = [];
const hubs = [];
const revokedUrls = [];
let nextUrl = 0;
let dom, vite, client, createRoot, TaskDiscussion, TaskCollaborationPanel, AuthContext;
let TaskCreationForm, TaskMlConsentDialog, createEmptyTaskCreationValue;
const task = { id: 9, ownerId: 1, canViewParticipants: true, title: "Local work" };
const messagesPath = "/TaskWorkspace/9/messages";
const deliveryPath = messagesPath + "/delivery";
const filesPath = "/TaskAttachment/task/9";
const limits = { uploadsEnabled: true, maxFiles: 3, maxFileSizeBytes: 20, maxTotalBytes: 40 };

before(async () => {
  dom = new JSDOM("<html><body></body></html>", { url: "http://localhost/" });
  const dialog = dom.window.HTMLDialogElement.prototype;
  dialog.showModal = function () { this.open = true; };
  dialog.close = function () { this.open = false; };
  for (const [key, value] of Object.entries({ window: dom.window, document: dom.window.document,
    Node: dom.window.Node, HTMLElement: dom.window.HTMLElement, localStorage: dom.window.localStorage,
    sessionStorage: dom.window.sessionStorage, FormData: dom.window.FormData, File: dom.window.File,
    IS_REACT_ACT_ENVIRONMENT: true })) {
    originals.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, value });
  }
  URL.createObjectURL = () => `blob:discussion-test-${++nextUrl}`;
  URL.revokeObjectURL = value => revokedUrls.push(value);
  HubConnectionBuilder.prototype.build = function () {
    const handlers = new Map();
    hubs.push(handlers);
    return { on: (name, callback) => handlers.set(name, callback), start: async () => {}, stop: async () => {},
      invoke: async () => {}, onreconnecting() {}, onreconnected() {}, onclose() {} };
  };
  ({ createRoot } = await import("react-dom/client"));
  process.env.VITE_API_BASE_URL = "http://localhost/api";
  vite = await createServer({
    root: fileURLToPath(new URL("..", import.meta.url)), configFile: false, appType: "custom",
    plugins: [{
      name: "discussion-test-styles", enforce: "pre",
      resolveId(source) { if (source.endsWith(".css")) return "\0discussion-test-style"; },
      load(id) { if (id === "\0discussion-test-style") return "export default {}"; },
    }],
    optimizeDeps: { noDiscovery: true }, server: { middlewareMode: true, hmr: false, ws: false, watch: null },
  });
  ({ default: client } = await vite.ssrLoadModule("/src/api/axiosClient.ts"));
  ({ default: TaskDiscussion } = await vite.ssrLoadModule("/src/components/TaskDiscussion.tsx"));
  ({ default: TaskCollaborationPanel } = await vite.ssrLoadModule("/src/components/TaskCollaborationPanel.tsx"));
  ({ AuthContext } = await vite.ssrLoadModule("/src/context/authContextValue.ts"));
  ({ default: TaskCreationForm } = await vite.ssrLoadModule("/src/components/TaskCreationForm.tsx"));
  ({ default: TaskMlConsentDialog } = await vite.ssrLoadModule("/src/components/TaskMlConsentDialog.tsx"));
  ({ createEmptyTaskCreationValue } = await vite.ssrLoadModule("/src/types/taskCreationForm.ts"));
});

afterEach(async () => {
  await act(async () => roots.splice(0).forEach(root => root.unmount()));
  document.body.replaceChildren();
  hubs.length = 0;
  revokedUrls.length = 0;
});

after(async () => {
  await vite?.close();
  HubConnectionBuilder.prototype.build = originalHubBuild;
  URL.createObjectURL = objectUrls.create;
  URL.revokeObjectURL = objectUrls.revoke;
  if (originalBase === undefined) delete process.env.VITE_API_BASE_URL;
  else process.env.VITE_API_BASE_URL = originalBase;
  dom?.window.close();
  for (const [key, descriptor] of originals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

function mockDiscussion({ settings = limits, onSend } = {}) {
  const messages = [];
  const files = [];
  const requests = [];
  client.defaults.adapter = async config => {
    requests.push(config);
    if (config.url === deliveryPath) return { data: await onSend?.(config, messages, files) };
    if (config.url === messagesPath + "/attachment-settings") return { data: settings };
    if (config.url === messagesPath) return { data: messages.slice() };
    if (config.url === filesPath) return { data: files.slice() };
    if (config.url === "/TaskWorkspace/9/activity") return { data: [] };
    if (config.url.startsWith("/TaskAttachment/") && config.url.endsWith("/download")) {
      assert.equal(config.responseType, "blob");
      return { data: new Blob(["preview"]) };
    }
    throw new Error(`Unexpected request ${config.url}`);
  };
  return requests;
}

async function openDiscussion(userId = 1) {
  const host = document.createElement("main");
  document.body.append(host);
  const root = createRoot(host);
  roots.push(root);
  const identifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
  const token = `head.${Buffer.from(JSON.stringify({ [identifier]: userId.toString() })).toString("base64url")}.sig`;
  const auth = { token, user: { role: "User", email: `user${userId}@example.test` } };
  function Harness() {
    const [revoked, setRevoked] = useState(false);
    const revoke = useCallback(() => setRevoked(true), []);
    return revoked ? h("p", null, "Access removed") :
      h(AuthContext.Provider, { value: auth }, h(TaskCollaborationPanel, {
        task, view: "discussion", onTaskChanged: () => undefined, onAccessRevoked: revoke,
      }, h(TaskDiscussion)));
  }
  await act(async () => root.render(h(Harness)));
  return host;
}

const button = (host, label) => [...host.querySelectorAll("button")].find(item => item.textContent.trim() === label);
const fileInput = host => host.querySelector('input[type="file"]');
const sent = requests => requests.filter(item => item.url === deliveryPath);

async function until(assertion) {
  let error;
  for (let attempt = 0; attempt < 80; attempt++) {
    try { assertion(); return; } catch (reason) { error = reason; }
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
  }
  throw error;
}

async function click(element) {
  assert.ok(element, "Expected an interactive element");
  await act(async () => element.click());
}

async function choose(host, ...files) {
  const input = fileInput(host);
  assert.ok(input?.multiple);
  Object.defineProperty(input, "files", { configurable: true, value: files });
  await act(async () => input.dispatchEvent(new dom.window.Event("change", { bubbles: true })));
}

async function enterText(host, text) {
  const textarea = host.querySelector("textarea");
  await act(async () => {
    Object.getOwnPropertyDescriptor(Object.getPrototypeOf(textarea), "value").set.call(textarea, text);
    textarea.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
  });
}

function receipt(id, content, count) {
  return { id, content, deliveryFileCount: count, senderUserId: 1, senderUserName: "Owner", createdAt: "2026-11-21T08:00:00Z" };
}

function attachment(id, messageId, name, mime) {
  return { id, taskRequestId: 9, taskMessageId: messageId, kind: "Message", fileName: name,
    contentType: mime, sizeBytes: 5, canDelete: false };
}

test("task creation defaults to no consent and submits with or without an explicit opt-in", async () => {
  const host = document.createElement("main");
  document.body.append(host);
  const root = createRoot(host);
  roots.push(root);
  const decisions = [];
  function Harness() {
    const [value, setValue] = useState(() => ({ ...createEmptyTaskCreationValue(),
      title: "Fixture task", description: "Complete draft", category: "Testing" }));
    return h(TaskCreationForm, {
      idPrefix: "test-task", value, onChange: setValue, onSubmit: draft => decisions.push(draft.mlConsentGranted),
      submitting: false, onCancel: () => {},
    });
  }
  await act(async () => root.render(h(Harness)));
  const checkbox = host.querySelector('input[name="mlConsentGranted"]');
  assert.equal(checkbox.checked, false);
  assert.equal(checkbox.required, false);
  await click(button(host, "Create task"));
  await click(checkbox);
  await click(button(host, "Create task"));
  assert.deepEqual(decisions, [false, true]);
});

test("completed-task consent settings submit an explicit revoke with the task version", async () => {
  const host = document.createElement("main");
  document.body.append(host);
  const root = createRoot(host);
  roots.push(root);
  const requests = [];
  let saved = 0;
  client.defaults.adapter = async config => {
    requests.push({ url: config.url, method: config.method, body: JSON.parse(config.data) });
    return { data: "Saved" };
  };
  await act(async () => root.render(h(TaskMlConsentDialog, {
    task: { id: 9, version: 4, mlConsent: { granted: true }, status: "Completed" },
    onClose: () => {}, refreshTask: async () => 5, onSaved: async () => { saved++; },
  })));
  const checkbox = host.querySelector('input[name="mlConsentGranted"]');
  assert.equal(checkbox.checked, true);
  assert.equal(button(host, "İzni kaydet").disabled, true);
  await click(checkbox);
  await click(button(host, "İzni kaydet"));
  assert.deepEqual(requests, [{ url: "/TaskRequest/9/ml-consent", method: "put", body: { granted: false, version: 4 } }]);
  assert.equal(saved, 1);
});

test("text-only messages remain available when uploads are disabled", async () => {
  const requests = mockDiscussion({ settings: { ...limits, uploadsEnabled: false },
    onSend: config => receipt(1, config.data.get("content"), 0) });
  const view = await openDiscussion();
  await until(() => assert.match(view.textContent, /uploads are unavailable/));
  assert.ok(!fileInput(view));
  assert.equal(button(view, "Send").disabled, true);
  await enterText(view, "Text only");
  const original = Object.getOwnPropertyDescriptor(crypto, "randomUUID");
  Object.defineProperty(crypto, "randomUUID", { configurable: true, value: undefined });
  try { await click(button(view, "Send")); }
  finally {
    if (original) Object.defineProperty(crypto, "randomUUID", original);
    else delete crypto.randomUUID;
  }
  await until(() => assert.equal(view.querySelector(".tc-message article p")?.textContent, "Text only"));
  assert.equal(sent(requests).length, 1);
  assert.match(sent(requests)[0].data.get("clientRequestId"), /^[\da-f]{8}-(?:[\da-f]{4}-){1}4[\da-f]{3}-[89ab][\da-f]{3}-[\da-f]{12}$/);
  assert.equal(sent(requests)[0].data.getAll("files").length, 0);
});

test("different files with matching names and metadata remain individually selectable", async () => {
  const requests = mockDiscussion({ onSend: config => receipt(2, config.data.get("content") ?? "", 2) });
  const view = await openDiscussion();
  await until(() => assert.ok(fileInput(view)));
  await choose(view, new File(["1"], "plan.txt", { lastModified: 1000 }), new File(["2"], "plan.txt", { lastModified: 1000 }));
  assert.equal(view.querySelectorAll('[aria-label="Remove plan.txt"]').length, 2);
  await click(button(view, "Send"));
  assert.deepEqual(sent(requests)[0].data.getAll("files").map(file => file.name), ["plan.txt", "plan.txt"]);
});

test("file-only messages preview selections, restore removal focus, and send once while showing progress", async () => {
  let finish;
  const requests = mockDiscussion({ onSend: async (config, messages, files) => {
    config.onUploadProgress({ loaded: 5, total: 10 });
    const item = await new Promise(resolve => { finish = resolve; });
    messages.push(item);
    files.push(attachment(15, item.id, "draft.txt", "text/plain"));
    return item;
  } });
  const view = await openDiscussion();
  await until(() => assert.ok(fileInput(view)));
  await choose(view, new File(["1"], "remove.txt"), new File(["2"], "draft.txt"));
  assert.match(view.querySelector('[aria-label="Selected attachments"]').textContent, /remove.txt.*draft.txt/);
  const remove = view.querySelector('[aria-label="Remove remove.txt"]');
  remove.focus();
  await click(remove);
  assert.equal(document.activeElement.getAttribute("aria-label"), "Remove draft.txt");
  await click(button(view, "Send"));
  await until(() => assert.equal(view.querySelector("progress")?.value, 50));
  await act(async () => view.querySelector("form").dispatchEvent(new dom.window.Event("submit", { bubbles: true, cancelable: true })));
  assert.equal(sent(requests).length, 1);
  assert.equal(sent(requests)[0].data.get("content"), null);
  assert.deepEqual(sent(requests)[0].data.getAll("files").map(file => file.name), ["draft.txt"]);
  await act(async () => finish(receipt(3, "", 1)));
  await until(() => assert.ok(view.querySelector('[aria-label="Download draft.txt"]')));
  assert.match(view.textContent, /Message and attachments sent/);
});

test("an uncertain send retries the same batch and a second viewer receives the message and both attachments", async () => {
  let saved;
  let attempted = false;
  const requests = mockDiscussion({ onSend: async (config, messages, files) => {
    if (!attempted) {
      attempted = true;
      saved = receipt(7, config.data.get("content"), 2);
      messages.push(saved);
      files.push(attachment(26, 7, "diagram.png", "image/png"), attachment(27, 7, "source.txt", "text/plain"));
      for (const hub of hubs) {
        hub.get("TaskMessageCreated")(saved);
        hub.get("TaskAttachmentsChanged")();
      }
      throw { isAxiosError: true, response: { status: 500, data: {} } };
    }
    return saved;
  } });
  const author = await openDiscussion();
  const reader = await openDiscussion(2);
  await until(() => { assert.equal(hubs.length, 2); assert.ok(fileInput(author)); });
  await enterText(author, "Two attachments");
  await choose(author, new File(["image"], "diagram.png", { type: "image/png" }), new File(["text"], "source.txt"));
  await click(button(author, "Send"));
  await until(() => assert.match(author.textContent, /Delivery could not be confirmed/));
  assert.equal(author.querySelector("textarea").disabled, true);
  await click(button(author, "Retry delivery"));
  await until(() => assert.ok(reader.querySelector('[aria-label="Download source.txt"]')));
  await until(() => assert.ok(reader.querySelector('img[alt="Preview of diagram.png"]')));
  assert.equal(reader.querySelectorAll(".tc-message").length, 1);
  assert.equal(author.querySelectorAll(".tc-message").length, 1);
  assert.match(reader.querySelector(".tc-message").textContent, /Two attachments/);
  const deliveries = sent(requests);
  assert.equal(deliveries.length, 2);
  assert.equal(deliveries[0].data.get("clientRequestId"), deliveries[1].data.get("clientRequestId"));
  assert.deepEqual(deliveries[1].data.getAll("files").map(file => file.name), ["diagram.png", "source.txt"]);
  const preview = reader.querySelector('img[alt="Preview of diagram.png"]').getAttribute("src");
  await act(async () => hubs[1].get("TaskAccessRevoked")(9));
  assert.match(reader.textContent, /Access removed/);
  assert.doesNotMatch(reader.textContent, /Two attachments|diagram.png/);
  assert.ok(revokedUrls.includes(preview));
});

test("invalid selections are explained and scanner failures keep the draft editable", async () => {
  const requests = mockDiscussion({ settings: { ...limits, maxFiles: 1, maxFileSizeBytes: 5 },
    onSend: () => { throw { isAxiosError: true, response: { status: 503,
      data: { code: "file_scan_unavailable", message: "File scanning is unavailable." } } }; } });
  const view = await openDiscussion();
  await until(() => assert.ok(fileInput(view)));
  await choose(view, new File(["text"], "program.exe"));
  assert.match(view.textContent, /program.exe.*Choose a PDF/);
  await choose(view, new File(["larger"], "large.txt"));
  assert.match(view.textContent, /large.txt.*5 B limit/);
  await choose(view, new File(["1"], "one.txt"), new File(["2"], "two.txt"));
  assert.match(view.textContent, /Choose at most 1/);
  await choose(view, new File(["okay"], "valid.txt"));
  await click(button(view, "Send"));
  await until(() => assert.match(view.textContent, /File scanning is unavailable/));
  assert.equal(view.querySelector("textarea").disabled, false);
  assert.ok(view.querySelector('[aria-label="Remove valid.txt"]'));
  assert.equal(sent(requests).length, 1);
});
