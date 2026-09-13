import { handleRequest } from "../../src/GamePatchKit.Cli/Functions/get-patch-version.ts";

const publishableKey = "sb_publishable_test_key";

function assert(condition: boolean): void {
  if (!condition) {
    throw new Error("Assertion failed");
  }
}

function assertEquals(actual: unknown, expected: unknown): void {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(
      `Expected ${JSON.stringify(expected)}, received ${
        JSON.stringify(actual)
      }`,
    );
  }
}

function functionRequest(
  query = "?bucket=game-a",
  method = "GET",
): Request {
  return new Request(`https://function.test/${query}`, { method });
}

async function assertError(
  response: Response,
  status: number,
  code: string,
): Promise<void> {
  assertEquals(response.status, status);
  const body = await response.json();
  assertEquals(body.error.code, code);
  assertEquals(typeof body.error.message, "string");
}

Deno.test("get-patch-version HTTP contract", async () => {
  const originalConsoleError = console.error;
  const errorLogs: string[] = [];
  console.error = (...data: unknown[]) => {
    errorLogs.push(data.map(String).join(" "));
  };

  let upstreamStatus = 200;
  let upstreamBody: unknown = [];
  const upstreamRequests: Array<{ Url: string; Headers: Headers }> = [];
  const server = Deno.serve(
    {
      hostname: "127.0.0.1",
      port: 0,
      onListen: () => {},
    },
    (request) => {
      upstreamRequests.push({
        Url: request.url,
        Headers: new Headers(request.headers),
      });
      return Response.json(upstreamBody, { status: upstreamStatus });
    },
  );
  Deno.env.set("SUPABASE_URL", `http://127.0.0.1:${server.addr.port}`);
  Deno.env.set(
    "SUPABASE_PUBLISHABLE_KEYS",
    JSON.stringify({ default: publishableKey }),
  );

  try {
    for (const method of ["POST", "PUT", "DELETE"]) {
      await assertError(
        await handleRequest(functionRequest("?bucket=game-a", method)),
        405,
        "method_not_allowed",
      );
    }

    for (
      const query of [
        "",
        "?bucket=",
        "?bucket=game-a&bucket=game-b",
        "?bucket=.",
        "?bucket=..",
        "?bucket=a/b",
      ]
    ) {
      await assertError(
        await handleRequest(functionRequest(query)),
        400,
        "invalid_bucket",
      );
    }

    assertEquals(upstreamRequests.length, 0);

    const projectUrl = Deno.env.get("SUPABASE_URL")!;
    Deno.env.delete("SUPABASE_URL");
    await assertError(
      await handleRequest(functionRequest()),
      500,
      "configuration_error",
    );
    Deno.env.set("SUPABASE_URL", projectUrl);

    for (
      const publishableKeys of [
        null,
        "invalid-json",
        "{}",
        '{"default":""}',
      ] as const
    ) {
      if (publishableKeys === null) {
        Deno.env.delete("SUPABASE_PUBLISHABLE_KEYS");
      } else {
        Deno.env.set("SUPABASE_PUBLISHABLE_KEYS", publishableKeys);
      }

      await assertError(
        await handleRequest(functionRequest()),
        500,
        "configuration_error",
      );
    }
    Deno.env.set(
      "SUPABASE_PUBLISHABLE_KEYS",
      JSON.stringify({ default: publishableKey }),
    );

    assertEquals(upstreamRequests.length, 0);

    for (
      const releaseVersion of [
        "0",
        "41",
        "42",
        "9007199254740993",
        "9223372036854775807",
      ]
    ) {
      upstreamStatus = 200;
      upstreamBody = [{ release_version: releaseVersion }];
      const request = functionRequest();
      request.headers.set("Authorization", "Bearer caller-secret");
      request.headers.set("apikey", "sb_publishable_caller_key");
      const response = await handleRequest(request);
      assertEquals(response.status, 200);
      assertEquals(await response.json(), {
        bucket: "game-a",
        releaseVersion,
        manifestPath: `manifests/${releaseVersion}.json`,
      });
    }

    upstreamBody = [];
    await assertError(
      await handleRequest(functionRequest()),
      404,
      "pointer_not_found",
    );

    for (const status of [400, 404, 409, 500]) {
      upstreamStatus = status;
      upstreamBody = {
        code: "POSTGREST_ERROR",
        details: null,
        hint: null,
        message: "upstream details must not be exposed",
      };
      await assertError(
        await handleRequest(functionRequest()),
        500,
        "query_failed",
      );
    }

    for (const status of [401, 403]) {
      upstreamStatus = status;
      await assertError(
        await handleRequest(functionRequest()),
        status,
        "query_failed",
      );
    }

    const requestCountBeforeTransientFailure = upstreamRequests.length;
    upstreamStatus = 503;
    await assertError(
      await handleRequest(functionRequest()),
      500,
      "query_failed",
    );
    assertEquals(
      upstreamRequests.length,
      requestCountBeforeTransientFailure + 1,
    );

    for (const request of upstreamRequests) {
      assert(
        request.Url.startsWith(
          `http://127.0.0.1:${server.addr.port}/rest/v1/gamepatch_pointer?`,
        ),
      );
      const url = new URL(request.Url);
      assertEquals(url.searchParams.get("bucket"), "eq.game-a");
      assertEquals(url.searchParams.get("select"), "release_version::text");
      assertEquals(request.Headers.get("apikey"), publishableKey);
      assert(request.Headers.get("Authorization") !== "Bearer caller-secret");
    }

    const logEvents = errorLogs.map((log) => JSON.parse(log));
    for (const event of logEvents) {
      assertEquals(event.function, "get-patch-version");
      assertEquals(typeof event.status, "number");
      assertEquals(typeof event.code, "string");
    }
    for (
      const code of [
        "method_not_allowed",
        "invalid_bucket",
        "configuration_error",
        "pointer_not_found",
        "query_failed",
      ]
    ) {
      assert(logEvents.some((event) => event.code === code));
    }
    const combinedLogs = errorLogs.join("\n");
    assert(!combinedLogs.includes(publishableKey));
    assert(!combinedLogs.includes("caller-secret"));
    assert(!combinedLogs.includes("upstream details"));
  } finally {
    console.error = originalConsoleError;
    Deno.env.delete("SUPABASE_URL");
    Deno.env.delete("SUPABASE_PUBLISHABLE_KEYS");
    await server.shutdown();
  }
});
