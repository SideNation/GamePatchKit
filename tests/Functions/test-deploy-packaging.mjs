import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { createServer } from "node:http";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";

// Verify the installed tool or single-file executable without sending a deployment to Supabase.
const executable = resolve(process.argv[2]);
const functionDirectory = new URL("../../src/GamePatchKit.Cli/Functions/", import.meta.url);
const functionFiles = await Promise.all([
    readFile(new URL("get-patch-version.ts", functionDirectory)),
    readFile(new URL("deno.json", functionDirectory)),
    readFile(new URL("deno.lock", functionDirectory)),
]);
let binary = await readFile(executable);
if (!functionFiles.every(file => binary.includes(file))) {
    const files = await readdir(dirname(executable), { recursive: true });
    const assemblies = files.filter(file => file.endsWith(`${process.platform === "win32" ? "\\" : "/"}gpk.dll`));
    assert.equal(assemblies.length, 1, "Expected one installed tool assembly");
    binary = await readFile(join(dirname(executable), assemblies[0]));
}
assert.ok(functionFiles.every(file => binary.includes(file)), "Packaged function files must exactly match their sources");

const proxy = createServer();
const destinations = [];
proxy.on("connect", (request, socket) => {
    destinations.push(request.url);
    socket.end("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
});
proxy.listen(0, "127.0.0.1");
await once(proxy, "listening");
const workingDirectory = await mkdtemp(join(tmpdir(), "gpk-deploy-packaging-"));
try {
    const proxyUrl = `http://127.0.0.1:${proxy.address().port}`;
    const token = "sbp_packaging_test_not_a_real_token";
    const child = spawn(executable, ["deploy-function", "--project-id", "abcdefghijklmnopqrst", "--access-token", token], {
        cwd: workingDirectory,
        env: {
            ...process.env,
            HTTPS_PROXY: proxyUrl, https_proxy: proxyUrl,
            HTTP_PROXY: proxyUrl, http_proxy: proxyUrl,
            ALL_PROXY: proxyUrl, all_proxy: proxyUrl,
            NO_PROXY: "", no_proxy: "",
        },
        timeout: 15000,
        stdio: ["ignore", "pipe", "pipe"],
    });
    let output = "";
    let error = "";
    child.stdout.on("data", data => { output += data; });
    child.stderr.on("data", data => { error += data; });
    const [exitCode] = await once(child, "close");
    assert.equal(exitCode, 1);
    assert.equal(output, "");
    assert.ok(error.includes("네트워크 오류"), "Expected the controlled transport failure after reading the resource");
    assert.ok(!error.includes(token));
    assert.ok(destinations.length > 0);
    assert.ok(destinations.every(destination => destination === "api.supabase.com:443"));
    console.log("Packaging: exact source embedded and read outside checkout; CLI exits 1 on controlled network failure without token output.");
} finally {
    proxy.close();
    await rm(workingDirectory, { recursive: true, force: true });
}
