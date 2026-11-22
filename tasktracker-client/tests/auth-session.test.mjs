import assert from "node:assert/strict";
import { after, before, beforeEach, test } from "node:test";
import { fileURLToPath } from "node:url";
import axios, { AxiosError } from "axios";
import { createServer } from "vite";

const baseUrl = process.env.VITE_API_BASE_URL;
const globalAdapter = axios.defaults.adapter;
const storage = new Map();
let vite, client, auth, revokeSession;

before(async () => {
  process.env.VITE_API_BASE_URL = "/api";
  globalThis.window = { location: { href: "/tasks" } };
  globalThis.sessionStorage = {
    getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value),
    removeItem: key => storage.delete(key),
  };
  vite = await createServer({
    root: fileURLToPath(new URL("..", import.meta.url)), configFile: false, appType: "custom",
    optimizeDeps: { noDiscovery: true },
    server: { middlewareMode: true, hmr: false, ws: false, watch: null },
  });
  ({ default: client } = await vite.ssrLoadModule("/src/api/axiosClient.ts"));
  auth = await vite.ssrLoadModule("/src/utils/authStorage.ts");
  ({ revokeSession } = await vite.ssrLoadModule("/src/api/authService.ts"));
});

beforeEach(() => {
  window.location.href = "/tasks";
  auth.startAuthSession("old-access", "old-refresh", "old-expiration");
  client.defaults.adapter = async config => {
    if (config.headers.get("Authorization") === "Bearer rotated-access")
      return { status: 200, data: config.url, headers: {}, config };
    throw new AxiosError("Unauthorized", undefined, config, undefined, { status: 401, config });
  };
});

after(async () => {
  await vite?.close();
  axios.defaults.adapter = globalAdapter;
  delete globalThis.window;
  delete globalThis.sessionStorage;
  if (baseUrl === undefined) delete process.env.VITE_API_BASE_URL;
  else process.env.VITE_API_BASE_URL = baseUrl;
});

function pauseRefresh() {
  const started = Promise.withResolvers();
  const completion = Promise.withResolvers();
  let calls = 0;
  axios.defaults.adapter = async config => {
    assert.equal(config.url, "/api/Auth/refresh-token");
    assert.deepEqual(JSON.parse(config.data), { refreshToken: "old-refresh" });
    calls += 1;
    started.resolve();
    await completion.promise;
    return { status: 200, headers: {}, config, data: { data: {
      accessToken: "rotated-access", refreshToken: "rotated-refresh", accessTokenExpiration: "rotated-expiration",
    } } };
  };
  return { started: started.promise, completion, calls: () => calls };
}

test("an in-flight refresh response cannot restore a signed-out session", async () => {
  const refresh = pauseRefresh();
  const rejected = assert.rejects(client.get("/protected"), { code: "ERR_CANCELED" });
  await refresh.started;
  auth.clearAuthTokens();
  refresh.completion.resolve();
  await rejected;
  assert.equal(auth.getAccessToken(), null);
  assert.equal(auth.getRefreshToken(), null);
  assert.equal(window.location.href, "/tasks");
});

test("an old refresh failure cannot clear or redirect a newer login", async () => {
  const refresh = pauseRefresh();
  const rejected = assert.rejects(client.get("/protected"), /outdated session/);
  await refresh.started;
  auth.clearAuthTokens();
  auth.startAuthSession("new-access", "new-refresh", "new-expiration");
  refresh.completion.reject(new Error("outdated session"));
  await rejected;
  assert.equal(auth.getAccessToken(), "new-access");
  assert.equal(auth.getRefreshToken(), "new-refresh");
  assert.equal(window.location.href, "/tasks");
});

test("parallel unauthorized requests share one refresh and both retry", async () => {
  const refresh = pauseRefresh();
  const requests = Promise.all([client.get("/first"), client.get("/second")]);
  await refresh.started;
  refresh.completion.resolve();
  const responses = await requests;
  assert.deepEqual(responses.map(response => response.data), ["/first", "/second"]);
  assert.equal(refresh.calls(), 1);
  assert.equal(auth.getRefreshToken(), "rotated-refresh");
});

test("server logout uses the captured refresh credential without a bearer interceptor", async () => {
  axios.defaults.adapter = async config => {
    assert.equal(config.url, "/api/Auth/logout");
    assert.deepEqual(JSON.parse(config.data), { refreshToken: "old-refresh" });
    assert.equal(config.headers.get("Authorization"), undefined);
    return { status: 204, headers: {}, config };
  };
  const credential = auth.getRefreshToken();
  auth.clearAuthTokens();
  await revokeSession(credential);
  assert.equal(auth.getAccessToken(), null);
});
