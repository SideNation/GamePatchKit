import { createClient } from "@supabase/supabase-js";

function failure(status: number, code: string): Response {
  console.error(
    JSON.stringify({ function: "get-patch-version", status, code }),
  );
  return Response.json({ error: { code, message: "Request failed." } }, {
    status,
  });
}

export async function handleRequest(request: Request): Promise<Response> {
  if (request.method !== "GET") {
    return failure(405, "method_not_allowed");
  }

  const buckets = new URL(request.url).searchParams.getAll("bucket");
  const bucket = buckets[0];
  if (
    buckets.length !== 1 || !bucket || /[^A-Za-z0-9._-]/.test(bucket) ||
    bucket === "." || bucket === ".."
  ) {
    return failure(400, "invalid_bucket");
  }

  const projectUrl = Deno.env.get("SUPABASE_URL");
  let publishableKey: unknown;
  try {
    publishableKey = JSON.parse(Deno.env.get("SUPABASE_PUBLISHABLE_KEYS") ?? "{}").default;
  } catch {
    return failure(500, "configuration_error");
  }

  if (!projectUrl || typeof publishableKey !== "string" || !publishableKey) {
    return failure(500, "configuration_error");
  }

  const supabase = createClient(projectUrl, publishableKey, {
    db: { retry: false },
    auth: {
      autoRefreshToken: false,
      persistSession: false,
      detectSessionInUrl: false,
    },
  });
  const { data: pointer, error, status } = await supabase
    .from("gamepatch_pointer").select("release_version::text").eq("bucket", bucket,).maybeSingle();
  if (error) {
    const responseStatus = status === 401 || status === 403 ? status : 500;
    return failure(responseStatus, "query_failed");
  }

  if (!pointer) {
    return failure(404, "pointer_not_found");
  }

  const releaseVersion = pointer.release_version as string;
  return Response.json({
    bucket,
    releaseVersion,
    manifestPath: `manifests/${releaseVersion}.json`,
  });
}

if (import.meta.main) {
  Deno.serve(handleRequest);
}
