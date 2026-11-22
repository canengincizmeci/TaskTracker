import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";
import { localCalendarDate } from "../src/utils/localCalendarDate.ts";
import { deliveryFileIds, matchingDeliveryFileIds, sameFiles, workflowSubmission } from "../src/utils/taskWorkflowState.ts";

test("the task workflow points to the exact revision, not the final item in a stale list", () => {
  const first = { id: 41, revisionNumber: 1 };
  const second = { id: 42, revisionNumber: 2 };
  assert.equal(workflowSubmission(42, [first]), undefined);
  assert.equal(workflowSubmission(42, [second, first]), second);
  assert.equal(workflowSubmission(null, [first, second]), undefined);
});

test("revision file snapshots detect late attachments without including reference or other revision files", () => {
  const files = [
    { id: 1, kind: "Task", taskSubmissionId: null },
    { id: 2, kind: "Submission", taskSubmissionId: 41, fileName: "notes.txt", sizeBytes: 8, uploaderUserId: 3 },
    { id: 3, kind: "Submission", taskSubmissionId: 42, fileName: "notes.txt", sizeBytes: 8, uploaderUserId: 3 },
    { id: 4, kind: "Submission", taskSubmissionId: 42, fileName: "appendix.txt", sizeBytes: 12, uploaderUserId: 3 },
  ];
  assert.deepEqual(deliveryFileIds(files, 42), [3, 4]);
  assert.equal(sameFiles([3, 4], [4, 3]), true);
  assert.equal(sameFiles([3], deliveryFileIds(files, 42)), false);
  assert.deepEqual(matchingDeliveryFileIds(files, 42, 3, " notes.txt ", 8), [3]);
  assert.deepEqual(matchingDeliveryFileIds(files, 42, 4, "notes.txt", 8), []);
});

test("local calendar day is stable at midnight across UTC offsets", () => {
  const original = process.env.TZ;
  try {
    process.env.TZ = "Europe/Istanbul";
    assert.equal(localCalendarDate(new Date("2026-12-31T20:59:59Z")), "2026-12-31");
    assert.equal(localCalendarDate(new Date("2026-12-31T21:00:00Z")), "2027-01-01");
    process.env.TZ = "Pacific/Kiritimati";
    assert.equal(localCalendarDate(new Date("2026-12-31T10:00:00Z")), "2027-01-01");
    process.env.TZ = "America/Los_Angeles";
    assert.equal(localCalendarDate(new Date("2027-01-01T07:59:59Z")), "2026-12-31");
    assert.equal(localCalendarDate(new Date("2027-01-01T08:00:00Z")), "2027-01-01");
  } finally {
    if (original === undefined) delete process.env.TZ;
    else process.env.TZ = original;
  }
});

let vite, client, createAttachmentCollection, getWorkspaceView;
const sessionStorageDescriptor = Object.getOwnPropertyDescriptor(globalThis, "sessionStorage");

before(async () => {
  Object.defineProperty(globalThis, "sessionStorage", { configurable: true, value: { getItem: () => null } });
  vite = await createServer({ root: fileURLToPath(new URL("..", import.meta.url)), configFile: false, appType: "custom",
    optimizeDeps: { noDiscovery: true }, server: { middlewareMode: true, hmr: false, ws: false, watch: null } });
  ({ default: client } = await vite.ssrLoadModule("/src/api/axiosClient.ts"));
  ({ createAttachmentCollection } = await vite.ssrLoadModule("/src/hooks/useTaskAttachments.ts"));
  ({ getWorkspaceView } = await vite.ssrLoadModule("/src/utils/taskWorkspaceViews.ts"));
});
after(async () => {
  await vite?.close();
  if (sessionStorageDescriptor) Object.defineProperty(globalThis, "sessionStorage", sessionStorageDescriptor);
  else delete globalThis.sessionStorage;
});

test("canonical tabs and old activity links resolve within the viewer's access", () => {
  assert.equal(getWorkspaceView({ search: "", hash: "#task-history" }, true), "activity");
  assert.equal(getWorkspaceView({ search: "?view=files&people=share", hash: "#task-history" }, true), "files");
  assert.equal(getWorkspaceView({ search: "?view=files", hash: "#task-discussion" }, false), "work");
  assert.equal(getWorkspaceView({ search: "?view=unknown", hash: "" }, true), "work");
});

test("concurrent consumers share a request and a committed upload invalidates an older read", async () => {
  const requests = [];
  client.defaults.adapter = config => new Promise(resolve => requests.push({ config, resolve }));
  const cache = createAttachmentCollection(4, "owner", assert.fail);
  const oldRead = cache.refresh();
  assert.equal(cache.refresh(), oldRead);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests.length, 1);
  const newRead = cache.invalidate();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests.length, 2);
  assert.equal(requests[0].config.signal.aborted, true);
  const files = [{ id: 2, taskRequestId: 4, fileName: "review.txt" }];
  requests[1].resolve({ data: files });
  await newRead;
  requests[0].resolve({ data: [] });
  await assert.rejects(oldRead);
  assert.deepEqual(await cache.refreshIfStale(), files);
  assert.deepEqual(cache.getSnapshot().files, files);
  assert.equal(requests.length, 2);
});

test("a denied list clears private files and closes the authorized workspace", async () => {
  let revoked = 0;
  const cache = createAttachmentCollection(4, "member", () => revoked++);
  client.defaults.adapter = async () => ({ data: [{ id: 3 }] });
  await cache.refresh();
  client.defaults.adapter = async () => { throw { isAxiosError: true, response: { status: 403 } }; };
  await assert.rejects(cache.refresh());
  assert.deepEqual(cache.getSnapshot().files, []);
  assert.equal(revoked, 1);
  assert.ok(cache.getSnapshot().error);
});

test("public task metadata never triggers a private attachment read", async () => {
  let requests = 0;
  client.defaults.adapter = async () => { requests++; return { data: [] }; };
  const cache = createAttachmentCollection(4, null, assert.fail);
  await assert.rejects(cache.refresh(), /unavailable/);
  assert.equal(requests, 0);
});

test("the display identity preserves UTF-8 names in a base64url JWT payload", async () => {
  const { decodeJwt } = await vite.ssrLoadModule("/src/utils/jwtHelper.ts");
  const claims = { name: "İpek Öğrenci", note: "📝" };
  const payload = Buffer.from(JSON.stringify(claims)).toString("base64url");
  assert.deepEqual(decodeJwt(`header.${payload}.signature`), claims);
  assert.equal(decodeJwt("header.%.signature"), null);
});

test("only pending invitations expire on the sender and recipient surfaces", async () => {
  const { invitationStatus } = await vite.ssrLoadModule("/src/hooks/useInvitationDeadline.ts");
  const expiresAt = "2026-11-17T12:00:00Z";
  const deadline = Date.parse(expiresAt);
  assert.equal(invitationStatus({ status: "Pending", expiresAt }, deadline - 1), "Pending");
  assert.equal(invitationStatus({ status: "Pending", expiresAt }, deadline), "Expired");
  for (const status of ["Accepted", "Rejected", "Cancelled"]) {
    assert.equal(invitationStatus({ status, expiresAt, respondedAt: expiresAt }, deadline + 1), status);
  }
});
