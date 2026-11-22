import assert from "node:assert/strict";
import { after, afterEach, before, test } from "node:test";
import { fileURLToPath } from "node:url";
import { act, createElement as h, useCallback, useState } from "react";
import { JSDOM } from "jsdom";
import { HubConnectionBuilder } from "@microsoft/signalr";
import { createServer } from "vite";

const originalHubBuild = HubConnectionBuilder.prototype.build;
const apiBase = process.env.VITE_API_BASE_URL;
const hubs = [];
let vite, client, dom, createRoot, MemoryRouter, useLocation, TaskWorkspace, TaskCollaborationPanel, AuthContext, DashboardPage, root;
const globals = new Map();
const taskId = 8;
const collection = `/TaskRequest/${taskId}/subtasks`;
const route = `/tasks/task-detail/${taskId}`;
const id = 27;
const resource = `${collection}/${id}`;
const initialParent = {
  id: taskId, title: "Parent task", description: "Parent description", category: "Tests", priority: "Medium",
  activity: true, dueDate: null, status: "InProgress", workspaceId: null, version: 97, ownerId: 1, ownerUserName: "Owner",
  assigneeUserId: null, assigneeUserName: null, isOwner: true, canViewParticipants: true, canManageResponsibility: true,
};
function item(overrides = {}) {
  return {
    id, taskRequestId: taskId, createdByUserId: 1, title: "Check citations", description: "Verify the references",
    assigneeUserId: null, assigneeUserName: null, status: "Pending", priority: "High", dueDate: "2099-04-07",
    createdAt: "2026-09-08T10:00:00Z", version: 3, currentSubmissionId: null,
    capabilities: { canEdit: true, canDelete: true, canAssign: true, canStart: true, canComplete: true, canCancel: true },
    ...overrides,
  };
}
function error(status, message) {
  return { isAxiosError: true, response: { status, data: { message } } };
}
function mockApi({ children = [], revisions = {}, activity = {}, files = {}, extra = {}, mutate, listError } = {}) {
  const requests = [];
  client.defaults.adapter = async config => {
    const body = typeof config.data === "string" ? JSON.parse(config.data) : config.data;
    const request = { method: config.method, url: config.url, body, params: config.params, signal: config.signal };
    requests.push(request);
    if (config.method !== "get") return { data: await mutate?.(request) };
    if (config.url === collection) {
      const failure = listError?.();
      if (failure) throw failure;
      return { data: children.slice() };
    }
    if (config.url.startsWith(collection + "/")) {
      const [, key, kind] = config.url.slice(collection.length).split("/");
      const child = children.find(value => value.id === Number(key));
      if (!child) throw error(404, "Not found");
      return { data: kind === "submissions" ? revisions[key] ?? [] : kind === "activity" ? activity[key] ?? [] :
        kind === "submission-files" ? files[key] ?? { version: child.version, uploadsEnabled: false, uploadSubmissionId: null, files: [] } : child };
    }
    if (Object.hasOwn(extra, config.url)) return { data: extra[config.url] };
    if (config.url.startsWith("/TaskWorkspace/") || config.url.startsWith("/TaskAttachment/") ||
        config.url === `/TaskRequest/${taskId}/submissions` || config.url.startsWith("/TaskShare/"))
      return { data: [] };
    throw new Error(`Unexpected request ${config.method} ${config.url}`);
  };
  return requests;
}

before(async () => {
  dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost/" });
  const environment = { window: dom.window, document: dom.window.document, sessionStorage: dom.window.sessionStorage,
    localStorage: dom.window.localStorage, Node: dom.window.Node, HTMLElement: dom.window.HTMLElement,
    FormData: dom.window.FormData, File: dom.window.File,
    IS_REACT_ACT_ENVIRONMENT: true };
  for (const [key, value] of Object.entries(environment)) {
    globals.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, value });
  }
  // Model the native dialog lifecycle and Escape's cancel event in the Node DOM.
  const dialog = dom.window.HTMLDialogElement.prototype;
  dialog.showModal = function () {
    this.open = true;
    this.querySelector("input, button")?.focus();
  };
  dialog.close = function () {
    this.open = false;
    if (this.contains(document.activeElement)) document.activeElement.blur();
  };
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && event.target.closest("dialog[open]")) {
      event.target.closest("dialog[open]").dispatchEvent(new dom.window.Event("cancel", { cancelable: true }));
    }
  });
  HubConnectionBuilder.prototype.build = function () {
    const handlers = new Map();
    const hub = { handlers, start: async () => {}, stop: async () => {}, invoke: async () => {},
      on: (event, handler) => handlers.set(event, handler), onreconnecting() {}, onreconnected() {}, onclose() {} };
    hubs.push(hub);
    return hub;
  };
  ({ createRoot } = await import("react-dom/client"));
  ({ MemoryRouter, useLocation } = await import("react-router-dom"));
  process.env.VITE_API_BASE_URL = "http://localhost/api";
  vite = await createServer({ root: fileURLToPath(new URL("..", import.meta.url)), configFile: false, appType: "custom",
    plugins: [{
      name: "dom-test-styles", enforce: "pre",
      resolveId(source) { if (source.endsWith(".css")) return "\0dom-test-style"; },
      load(id) { if (id === "\0dom-test-style") return "export default {}"; },
    }],
    optimizeDeps: { noDiscovery: true, include: [] }, server: { middlewareMode: true, hmr: false, ws: false, watch: null } });
  ({ default: client } = await vite.ssrLoadModule("/src/api/axiosClient.ts"));
  ({ default: TaskWorkspace } = await vite.ssrLoadModule("/src/components/TaskWorkspace.tsx"));
  ({ default: TaskCollaborationPanel } = await vite.ssrLoadModule("/src/components/TaskCollaborationPanel.tsx"));
  ({ AuthContext } = await vite.ssrLoadModule("/src/context/authContextValue.ts"));
  ({ default: DashboardPage } = await vite.ssrLoadModule("/src/pages/DashboardPage.tsx"));
});

afterEach(async () => {
  if (root) await act(async () => root.unmount());
  root = undefined;
  document.body.replaceChildren();
  hubs.length = 0;
});

after(async () => {
  await vite?.close();
  if (apiBase === undefined) delete process.env.VITE_API_BASE_URL;
  else process.env.VITE_API_BASE_URL = apiBase;
  HubConnectionBuilder.prototype.build = originalHubBuild;
  dom?.window.close();
  for (const [key, descriptor] of globals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor);
    else delete globalThis[key];
  }
});

async function render(element, entry = route) {
  const host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
  await act(async () => root.render(h(MemoryRouter, { initialEntries: [entry] }, element)));
}

async function workspace(parent = {}, entry = route, userId = 1) {
  const task = { ...initialParent, ...parent };
  const refreshTask = async () => task.version;
  const token = `fixtures.${Buffer.from(JSON.stringify({
    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": String(userId),
  })).toString("base64url")}.token`;
  function Harness() {
    const [revoked, revoke] = useState(false);
    const accessRevoked = useCallback(() => revoke(true), []);
    const { search } = useLocation();
    const view = new URLSearchParams(search).get("view") ?? "work";
    if (revoked) return h("p", null, "Access removed");
    return h(AuthContext.Provider, { value: { token, user: { role: "User", email: "fixture@example.test" } } },
      h(TaskCollaborationPanel, { task, view, onTaskChanged: refreshTask, onAccessRevoked: accessRevoked },
        h(TaskWorkspace, { task, view, refreshTask, refreshFailed: false })));
  }
  await render(h(Harness), entry);
}

const text = () => document.body.textContent;
const button = (label, scope = document) => [...scope.querySelectorAll("button")].find(element => element.textContent.trim() === label);
const field = (label, scope = document) => [...scope.querySelectorAll("label")]
  .find(element => element.firstChild?.textContent.trim() === label)?.control;
const dialog = () => document.querySelector("dialog[open]");
const detail = () => document.querySelector(".st-detail");
const writes = requests => requests.filter(request => request.method !== "get");
const reads = (requests, url) => requests.filter(request => request.method === "get" && request.url === url);
async function until(check) {
  let failure;
  for (let attempt = 0; attempt < 80; attempt++) {
    try { check(); return; } catch (reason) { failure = reason; }
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
  }
  throw failure;
}
async function click(element) {
  assert.ok(element, "Expected an interactive element");
  await act(async () => element.click());
}
async function fill(element, value) {
  assert.ok(element, "Expected a labelled field");
  await act(async () => {
    Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value").set.call(element, value);
    element.dispatchEvent(new dom.window.Event(element.tagName === "SELECT" ? "change" : "input", { bubbles: true }));
  });
}

test("creates and displays a subtask, preserves zero-subtask parent controls, and restores dialog focus on Escape", async () => {
  const children = [];
  const requests = mockApi({ children, mutate: ({ body }) => {
    const created = item({ ...body });
    children.push(created);
    return created;
  } });
  await workspace();
  await until(() => assert.equal(button("Complete task")?.disabled, false));
  assert.match(text(), /No subtasks yet/);
  assert.doesNotMatch(text(), /all open subtasks are resolved/);
  const opener = button("New subtask");
  opener.focus();
  await click(opener);
  assert.equal(document.getElementById(dialog().getAttribute("aria-labelledby")).textContent, "Create subtask");
  assert.ok(dialog().contains(document.activeElement));
  await act(async () => document.activeElement.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
  assert.ok(!dialog(), "Escape closes the dialog");
  assert.ok(document.activeElement === opener, "Escape restores focus to the opener");
  await click(opener);
  await fill(field("Title", dialog()), "Check citations");
  await fill(field("Description", dialog()), "Verify the references");
  await fill(field("Priority", dialog()), "High");
  await fill(field("Due date", dialog()), "2099-04-07");
  await click(button("Create subtask", dialog()));
  await until(() => assert.equal(detail()?.querySelector("h3").textContent, "Check citations"));
  assert.ok(document.activeElement === detail().querySelector("h3"), "Creation focuses the selected detail heading");
  assert.match(text(), /0 of 1 completed/);
  assert.match(text(), /High priority/);
  assert.match(text(), /Unassigned/);
  assert.equal(button("Complete task").disabled, true);
  assert.match(text(), /all open subtasks are resolved/);
  assert.deepEqual(writes(requests).map(({ method, url }) => [method, url]), [["post", collection]]);
  await click(button("Close details"));
  assert.ok(document.activeElement === button("Check citations"), "Closing details restores focus to the row");
});

test("assignment lists eligible users and sends the child version for assignment and unassignment", async () => {
  const children = [item()];
  const requests = mockApi({ children, extra: { [`/TaskShare/task-participants/${taskId}`]: [
    { userId: 4, userName: "Editor", permission: "Edit" }, { userId: 5, userName: "Viewer", permission: "View" },
  ] }, mutate: ({ body }) => {
    children[0] = item({ version: children[0].version + 1, assigneeUserId: body.assigneeUserId ?? null,
      assigneeUserName: body.assigneeUserId ? "Editor" : null });
  } });
  await workspace({}, route + "?subtask=" + id);
  await until(() => assert.equal(button("Assign subtask")?.disabled, false));
  await click(button("Assign subtask"));
  await until(() => assert.equal(field("Assignee", dialog())?.disabled, false));
  assert.deepEqual([...field("Assignee", dialog()).options].map(option => option.text), ["Unassigned", "Owner", "Editor"]);
  await fill(field("Assignee", dialog()), "4");
  await click(button("Save assignment", dialog()));
  await until(() => assert.equal(button("Change assignee")?.disabled, false));
  assert.match(detail().textContent, /Editor/);
  await click(button("Change assignee"));
  await until(() => assert.equal(field("Assignee", dialog())?.disabled, false));
  await fill(field("Assignee", dialog()), "");
  await click(button("Unassign subtask", dialog()));
  assert.deepEqual(writes(requests).map(({ url, body }) => [url, body]), [
    [resource + "/assign", { version: 3, assigneeUserId: 4 }],
    [resource + "/unassign", { version: 4 }],
  ]);
});

test("creating with an eligible assignee uses a single request", async () => {
  const children = [];
  const requests = mockApi({ children, extra: { [`/TaskShare/task-participants/${taskId}`]: [
    { userId: 4, userName: "Editor", permission: "Edit" }, { userId: 5, userName: "Viewer", permission: "View" },
  ] }, mutate: ({ body }) => {
    const created = item({ ...body, assigneeUserName: "Editor" });
    children.push(created);
    return created;
  } });
  await workspace();
  await until(() => assert.equal(button("New subtask")?.disabled, false));
  await click(button("New subtask"));
  await until(() => assert.equal(field("Assignee", dialog())?.disabled, false));
  assert.match(dialog().textContent, /View-only collaborators and pending invitees are ineligible/);
  assert.ok(![...field("Assignee", dialog()).options].some(option => option.text === "Viewer"));
  await fill(field("Title", dialog()), "Assigned at creation");
  await fill(field("Description", dialog()), "Assigned with the first save");
  await fill(field("Assignee", dialog()), "4");
  await click(button("Create subtask", dialog()));
  await until(() => assert.match(detail()?.textContent ?? "", /Editor/));
  assert.deepEqual(writes(requests).map(({ url, body }) => [url, body.assigneeUserId]), [[collection, 4]]);
});

test("delegated submission retains its draft across selection and a 409, then sends only after an explicit retry", async () => {
  const children = [item({ assigneeUserId: 4, assigneeUserName: "Editor", capabilities: { canStart: true } }),
    item({ id: 29, title: "Other subtask", capabilities: {} })];
  let rejectSubmission = true;
  const requests = mockApi({ children, mutate: ({ url }) => {
    if (url.endsWith("/start")) { children[0] = { ...children[0], status: "InProgress", version: 4, capabilities: { canSubmit: true } }; return; }
    if (rejectSubmission) {
      rejectSubmission = false;
      children[0] = { ...children[0], version: 5 };
      throw error(409, "The subtask changed.");
    }
    children[0] = { ...children[0], version: 6, status: "InReview", capabilities: {} };
  } });
  await workspace({ isOwner: false, canManageResponsibility: false, currentUserPermission: "Edit" }, route + "?subtask=" + id, 4);
  await until(() => assert.equal(button("Start subtask")?.disabled, false));
  assert.ok(!button("Assign subtask"));
  await click(button("Start subtask"));
  await until(() => assert.ok(field("Subtask work summary")));
  await fill(field("Subtask work summary"), "Draft linked to this subtask");
  await click(button("Other subtask"));
  await until(() => assert.equal(detail()?.querySelector("h3").textContent, "Other subtask"));
  await click(button("Check citations"));
  await until(() => assert.equal(field("Subtask work summary")?.value, "Draft linked to this subtask"));
  await click(button("Submit subtask for review"));
  await until(() => assert.equal(button("Submit subtask for review")?.disabled, false));
  assert.match(detail().textContent, /The subtask changed.*Check the latest state/);
  assert.equal(writes(requests).length, 2);
  assert.equal(field("Subtask work summary").value, "Draft linked to this subtask");
  await click(button("Submit subtask for review"));
  await until(() => assert.match(detail().textContent, /Waiting for review/));
  assert.doesNotMatch(detail().textContent, /Unsent subtask draft|Attach delivery file/);
  assert.deepEqual(writes(requests).map(({ url, body }) => [url, body]), [
    [resource + "/start", { version: 3 }],
    [resource + "/submissions", { version: 4, content: "Draft linked to this subtask" }],
    [resource + "/submissions", { version: 5, content: "Draft linked to this subtask" }],
  ]);
});

test("review actions name revisions and use the child's current submission even when the parent has another revision", async () => {
  const previous = { id: 121, revisionNumber: 1, submittedByUserName: "Editor", createdAt: "2026-11-19T10:00:00Z",
    content: "First pass", review: { decision: "ChangesRequested", feedback: "Correct the citations",
      reviewerUserName: "Owner", createdAt: "2026-11-19T11:00:00Z" } };
  const current = { ...previous, id: 125, revisionNumber: 2, content: "Second pass", review: null };
  const children = [item({ status: "InReview", currentSubmissionId: current.id, capabilities: { canReview: true } })];
  const history = [current, previous];
  const requests = mockApi({ children, revisions: { [id]: history }, activity: { [id]: [
    { id: 14, createdAt: current.createdAt, actorUserName: "Editor", activityType: "SubmissionCreated", submissionId: current.id },
  ] }, mutate: ({ body }) => {
    children[0] = { ...children[0], version: children[0].version + 1, currentSubmissionId: null,
      status: body.decision === "Approved" ? "Completed" : "InProgress", capabilities: {} };
  } });
  await workspace({ workflowSubmissionId: 9001 }, route + "?subtask=" + id);
  await until(() => assert.equal(button("Approve subtask")?.disabled, false));
  assert.match(detail().textContent, /Review revision 2/);
  assert.match(detail().textContent, /Revision 1/);
  assert.match(detail().textContent, /Correct the citations/);
  assert.match(detail().textContent, /Editor submitted work · Revision 2/);
  assert.doesNotMatch(detail().textContent, /\b125\b|\b9001\b/);
  assert.equal(button("Request changes", detail()).disabled, true);
  await fill(field("Subtask review feedback"), "One citation still needs a page number");
  await click(button("Request changes", detail()));
  await until(() => assert.match(detail().textContent, /Changes requested\./));
  const next = { ...current, id: 126, revisionNumber: 3, content: "Final pass" };
  history.unshift(next);
  children[0] = { ...children[0], version: 6, currentSubmissionId: next.id, status: "InReview", capabilities: { canReview: true } };
  await act(async () => hubs[0].handlers.get("TaskChanged")(taskId));
  await until(() => assert.match(detail().textContent, /Review revision 3/));
  await click(button("Approve subtask"));
  assert.match(dialog().textContent, /parent task keeps its current status/);
  await click(button("Confirm approval", dialog()));
  await until(() => assert.match(text(), /1 of 1 completed/));
  assert.equal(button("Complete task").disabled, false);
  assert.deepEqual(writes(requests).map(({ url, body }) => [url, body]), [
    [resource + "/submissions/125/review", { version: 3, decision: "ChangesRequested", feedback: "One citation still needs a page number" }],
    [resource + "/submissions/126/review", { version: 6, decision: "Approved", feedback: null }],
  ]);
});

test("403 refreshes capabilities, removes unauthorized controls and explains the denial", async () => {
  const children = [item({ capabilities: { canStart: true } })];
  const requests = mockApi({ children, mutate: () => {
    children[0] = { ...children[0], capabilities: {} };
    throw error(403, "Forbidden");
  } });
  await workspace({}, route + "?subtask=" + id);
  await until(() => assert.equal(button("Start subtask")?.disabled, false));
  await click(button("Start subtask"));
  await until(() => assert.ok(!button("Start subtask")));
  assert.match(detail().textContent, /current permissions do not allow/);
  assert.equal(writes(requests).length, 1);
  assert.ok(reads(requests, resource).length >= 2);
});

test("the existing SignalR handler coalesces task refreshes and discards private data on revocation", async () => {
  const children = [item()];
  const requests = mockApi({ children });
  await workspace({}, route + "?subtask=" + id);
  await until(() => assert.equal(button("Start subtask")?.disabled, false));
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 100)); });
  const original = reads(requests, collection).length;
  const selectedReads = reads(requests, resource).length;
  assert.equal(hubs.length, 1);
  children[0] = item({ version: 8, title: "Updated from another client" });
  await act(async () => {
    const changed = hubs[0].handlers.get("TaskChanged");
    changed(taskId + 1);
    changed(taskId);
    changed(taskId);
  });
  await until(() => assert.equal(detail()?.querySelector("h3").textContent, "Updated from another client"));
  assert.equal(reads(requests, collection).length, original + 1);
  assert.equal(reads(requests, resource).length, selectedReads + 1);
  assert.equal(hubs.length, 1);
  await act(async () => hubs[0].handlers.get("TaskAccessRevoked")(taskId));
  assert.match(text(), /Access removed/);
  assert.doesNotMatch(text(), /Updated from another client|Verify the references/);
});

test("an in-review parent blocks approval while leaving parent changes requested available and subtasks read-only", async () => {
  mockApi({ children: [item()], extra: { [`/TaskRequest/${taskId}/submissions`]: [
    { id: 46, revisionNumber: 1, content: "Parent delivery", createdAt: "2026-11-19T09:00:00Z", submittedByUserId: 4,
      submittedByUserName: "Editor", review: null },
  ] } });
  await workspace({ status: "InReview", canReview: true, workflowSubmissionId: 46, assigneeUserId: 4,
    assigneeUserName: "Editor" }, route + "?subtask=" + id);
  await until(() => assert.equal(button("Request changes")?.disabled, false));
  assert.equal(button("Approve & complete")?.disabled, true);
  assert.ok(!button("New subtask"));
  assert.ok(!button("Start subtask"));
  assert.match(text(), /read-only while the parent is in review/);
});

test("the default dashboard uses one paged work feed for subtasks, filters, and refresh", async () => {
  const requests = mockApi({ extra: {
    "/work-dashboard/summary": { assignedToMeCount: 1, awaitingMyReviewCount: 0, overdueCount: 0,
      pendingTaskInvitationsCount: 0, pendingWorkspaceInvitationsCount: 0 },
    "/work-dashboard/tasks": { page: 1, pageSize: 20, totalCount: 1, totalPages: 1, items: [{
      taskId, subTaskId: id, parentTitle: "Parent task", title: "Assigned child", priority: "High",
      status: "Pending", dueDate: null, nextAction: "Start", isAssigned: true, ownerUserName: "Owner",
    }] } } });
  await render(h(DashboardPage), "/dashboard");
  await until(() => assert.equal(document.querySelector(".dashboard-work-item h3 a")?.getAttribute("href"), route + "?subtask=" + id));
  assert.equal(document.querySelectorAll(".dashboard-work-item").length, 1);
  assert.match(text(), /Assigned child.*Subtask of Parent task/);
  await click(button("Assigned"));
  await until(() => assert.equal(reads(requests, "/work-dashboard/tasks").at(-1)?.params.scope, "assigned"));
  await fill(field("Priority"), "High");
  await until(() => assert.equal(reads(requests, "/work-dashboard/tasks").at(-1)?.params.priority, "High"));
  const prior = reads(requests, "/work-dashboard/tasks").length;
  await click(document.querySelector('[aria-label="Refresh work"]'));
  await until(() => assert.equal(reads(requests, "/work-dashboard/tasks").length, prior + 1));
  assert.equal(reads(requests, "/TaskRequest/assigned-subtasks").length, 0);
});

test("review feedback stays isolated per revision and selection without refetching or stealing focus on tab return", async () => {
  const children = [item({ status: "InReview", currentSubmissionId: 41, capabilities: { canReview: true } }),
    item({ id: 28, title: "Another subtask", status: "InReview", currentSubmissionId: 42, capabilities: { canReview: true } })];
  const revision = id => ({ id, revisionNumber: 1, content: "Ready", submittedByUserName: "Editor",
    createdAt: "2026-11-19T10:00:00Z", review: null });
  const requests = mockApi({ children, revisions: { 27: [revision(41)], 28: [revision(42)] } });
  await workspace({}, route + "?subtask=" + id);
  await until(() => assert.equal(button("Approve subtask")?.disabled, false));
  await fill(field("Subtask review feedback"), "Keep the original source reference");
  const readCount = reads(requests, resource).length;
  const tab = name => document.getElementById("tab-" + name);
  for (const name of ["files", "discussion", "activity", "work"]) {
    tab(name).focus();
    await click(tab(name));
  }
  assert.equal(field("Subtask review feedback").value, "Keep the original source reference");
  assert.equal(reads(requests, resource).length, readCount);
  assert.ok(document.activeElement === tab("work"), "Returning to Work keeps keyboard focus on its tab");
  await click(button("Another subtask"));
  await until(() => assert.equal(detail()?.querySelector("h3").textContent, "Another subtask"));
  assert.equal(field("Subtask review feedback").value, "");
  await fill(field("Subtask review feedback"), "Other feedback");
  await click(button("Check citations"));
  await until(() => assert.equal(field("Subtask review feedback")?.value, "Keep the original source reference"));
});

test("a missing or temporarily unavailable subtask list preserves parent access until revocation is confirmed", async () => {
  let status = 404;
  mockApi({ children: [item()], listError: () => status ? error(status, "Unavailable") : undefined });
  await workspace();
  await until(() => assert.match(text(), /The subtask list was not found/));
  assert.match(text(), /Parent task/);
  assert.doesNotMatch(text(), /Access removed|Check citations/);
  status = 500;
  await click(button("Retry subtasks"));
  await until(() => assert.match(text(), /Could not load subtasks/));
  assert.match(text(), /Parent task/);
  status = 0;
  await click(button("Retry subtasks"));
  await until(() => assert.match(text(), /Check citations/));
  status = 403;
  await act(async () => hubs[0].handlers.get("TaskChanged")(taskId));
  await until(() => assert.match(text(), /Access removed/));
  assert.doesNotMatch(text(), /Check citations/);
});

test("an authorized assignee sees the active delivery, files, revisions, and inline reviews without owner forms", async () => {
  const revision = { id: 201, revisionNumber: 2, content: "Updated references", submittedByUserId: 4,
    submittedByUserName: "Editor", createdAt: "2026-11-19T10:00:00Z", review: null };
  const children = [item({ status: "InReview", currentSubmissionId: revision.id, assigneeUserId: 4, capabilities: {} })];
  const history = [revision];
  const files = { [id]: { version: 3, uploadsEnabled: true, uploadSubmissionId: revision.id, files: [
    { id: 7, subTaskSubmissionId: 200, fileName: "old-revision.txt", sizeBytes: 10, canDelete: false },
  ] } };
  const requests = mockApi({ children, revisions: { [id]: history }, files, mutate: ({ body }) => {
    assert.equal(body.get("file").name, "current-revision.txt");
    const next = { id: 8, subTaskSubmissionId: 201, fileName: "current-revision.txt", sizeBytes: 10, canDelete: false };
    children[0] = { ...children[0], version: 4 };
    files[id] = { ...files[id], version: 4, files: [...files[id].files, next] };
  } });
  await workspace({ isOwner: false, canManageResponsibility: false }, route + "?subtask=" + id, 4);
  const delivery = () => detail()?.querySelector('[aria-label="Latest subtask submission"]');
  await until(() => assert.match(delivery()?.textContent ?? "", /Awaiting review · Revision 2/));
  assert.ok(!button("Approve subtask"));
  assert.doesNotMatch(delivery().textContent, /old-revision.txt/);
  const input = delivery().querySelector('input[type="file"]');
  Object.defineProperty(input, "files", { configurable: true, value: [new File(["Evidence"], "current-revision.txt")] });
  await act(async () => input.dispatchEvent(new dom.window.Event("change", { bubbles: true })));
  await click(button("Upload delivery file"));
  await act(async () => hubs[0].handlers.get("TaskChanged")(taskId));
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 100)); });
  await until(() => assert.match(delivery().textContent, /current-revision.txt/));
  assert.equal(reads(requests, resource + "/submission-files").length, 2, "initial load + one refresh after upload and SignalR echo");
  assert.ok(reads(requests, resource + "/submission-files").every(request => !request.signal?.aborted));
  assert.deepEqual(writes(requests).map(({ url, params }) => [url, params]), [
    [resource + "/submissions/201/files", { version: 3 }],
  ]);
  history[0] = { ...revision, review: { decision: "ChangesRequested", feedback: "Add a source",
    reviewerUserName: "Owner", createdAt: revision.createdAt } };
  children[0] = { ...children[0], status: "InProgress", version: 5, currentSubmissionId: null };
  files[id] = { ...files[id], version: 5, uploadSubmissionId: null };
  await act(async () => hubs[0].handlers.get("TaskChanged")(taskId));
  await until(() => assert.match(delivery().textContent, /Changes requested · Revision 2.*Add a source/));
  history[0] = { ...history[0], review: { ...history[0].review, decision: "Approved", feedback: "Verified" } };
  children[0] = { ...children[0], status: "Completed", version: 6 };
  files[id] = { ...files[id], version: 6 };
  await act(async () => hubs[0].handlers.get("TaskChanged")(taskId));
  await until(() => assert.match(delivery().textContent, /Approved revision 2.*Verified/));
});

test("request changes with a SignalR echo and duplicate TaskChanged events avoid duplicate detail fetches while rapid switching aborts stale detail loads", async () => {
  const current = { id: 125, revisionNumber: 1, submittedByUserName: "Editor", createdAt: "2026-11-19T10:00:00Z",
    content: "Ready for review", review: null };
  const children = [
    item({ status: "InReview", currentSubmissionId: current.id, capabilities: { canReview: true } }),
    item({ id: 28, title: "Sibling subtask", status: "Pending", version: 1, capabilities: { canStart: true } }),
  ];
  const history = [current];
  const requests = mockApi({ children, revisions: { [id]: history, 28: [] }, mutate: ({ body }) => {
    history[0] = { ...current, review: { decision: body.decision, feedback: body.feedback,
      reviewerUserName: "Owner", createdAt: current.createdAt } };
    children[0] = { ...children[0], version: children[0].version + 1, currentSubmissionId: null,
      status: "InProgress", capabilities: {} };
    hubs[0]?.handlers.get("TaskChanged")?.(taskId);
  } });
  await workspace({}, route + "?subtask=" + id);
  await until(() => assert.equal(button("Approve subtask")?.disabled, false));
  const initialDetailReads = reads(requests, resource).length;
  const initialFileReads = reads(requests, resource + "/submission-files").length;
  assert.equal(initialDetailReads, 1);
  assert.equal(initialFileReads, 1);

  await fill(field("Subtask review feedback"), "Cite chapter 4 explicitly");
  await click(button("Request changes", detail()));
  await until(() => assert.match(detail().textContent, /Changes requested\./));
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 110)); });

  assert.equal(reads(requests, resource).length, initialDetailReads + 1, "local reload + SignalR echo fetches updated detail once");
  assert.equal(reads(requests, resource + "/submission-files").length, initialFileReads + 1, "submission-files is fetched once without abort race");
  assert.ok(reads(requests, resource + "/submission-files").every(request => !request.signal?.aborted));

  // Duplicate SignalR TaskChanged or sibling subtask update does not re-fetch the unchanged selected subtask details
  children[1] = { ...children[1], version: 2, title: "Updated sibling subtask" };
  await act(async () => {
    hubs[0].handlers.get("TaskChanged")(taskId);
    hubs[0].handlers.get("TaskChanged")(taskId);
  });
  await until(() => assert.ok(button("Updated sibling subtask")));
  assert.equal(reads(requests, resource).length, initialDetailReads + 1);
  assert.equal(reads(requests, resource + "/submission-files").length, initialFileReads + 1);

  // Rapid switching between subtasks aborts the first in-flight detail batch and renders only the latest selection
  let releaseSlowDetail;
  const slowDetailGate = new Promise(resolve => { releaseSlowDetail = resolve; });
  const baseAdapter = client.defaults.adapter;
  client.defaults.adapter = async config => {
    const response = baseAdapter(config);
    if (config.method === "get" && config.url.startsWith(`${collection}/28`)) {
      await slowDetailGate;
    }
    return response;
  };
  await click(button("Updated sibling subtask"));
  const siblingRequests = reads(requests, `${collection}/28/submission-files`);
  assert.equal(siblingRequests.length, 1);
  await click(button("Check citations"));
  assert.equal(siblingRequests[0].signal?.aborted, true, "Switching away aborts in-flight detail requests for the previous subtask");
  releaseSlowDetail();
  await until(() => assert.equal(detail()?.querySelector("h3").textContent, "Check citations"));
});
