namespace Briscola.Api;

/// <summary>
/// Static HTML pages served by the dev-only auto-docs block in
/// <c>Program.cs</c>: a small <c>/docs</c> landing index and the
/// <c>/docs/asyncapi</c> viewer. Both live as embedded string
/// constants so they don't have to navigate the Web SDK's
/// content-vs-static-asset routing rules.
/// </summary>
internal static class DocsLanding
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>Briscola API — Docs</title>
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <style>
            body { font-family: ui-sans-serif, system-ui, sans-serif; max-width: 720px; margin: 4rem auto; padding: 0 1.5rem; color: #1a1a1a; }
            h1 { margin-bottom: 0.25rem; }
            p.lede { color: #555; margin-top: 0; }
            ul { padding: 0; list-style: none; display: grid; gap: 1rem; margin-top: 2rem; }
            li { border: 1px solid #ddd; border-radius: 8px; padding: 1rem 1.25rem; }
            li a { font-weight: 600; text-decoration: none; color: #0b6bcb; }
            li a:hover { text-decoration: underline; }
            li small { display: block; color: #555; margin-top: 0.25rem; }
            code { background: #f4f4f5; padding: 0 0.25rem; border-radius: 3px; }
          </style>
        </head>
        <body>
          <h1>Briscola API</h1>
          <p class="lede">Auto-generated documentation. Pick a UI.</p>
          <ul>
            <li>
              <a href="/scalar">Scalar</a>
              <small>Modern interactive UI for the REST surface. Built-in code samples (curl, JS, Python, C#, Go, …).</small>
            </li>
            <li>
              <a href="/swagger">Swagger UI</a>
              <small>Classic OpenAPI explorer with a try-it-out console. Same spec as Scalar.</small>
            </li>
            <li>
              <a href="/swagger/v1/swagger.json">OpenAPI v3 JSON</a>
              <small>Raw spec for codegen tools (<code>openapi-generator</code>, <code>NSwag</code>, …).</small>
            </li>
            <li>
              <a href="/docs/asyncapi">AsyncAPI viewer</a>
              <small>SignalR hubs <code>/hubs/lobby</code> and <code>/hubs/game</code>. Channels, operations, and event shapes.</small>
            </li>
            <li>
              <a href="/docs/asyncapi.json">AsyncAPI v3 JSON</a>
              <small>Raw spec — the SignalR companion to <code>/swagger/v1/swagger.json</code>.</small>
            </li>
          </ul>
        </body>
        </html>
        """;

    /// <summary>
    /// AsyncAPI viewer. Pulls the <c>@asyncapi/react-component</c>
    /// standalone bundle from a CDN at runtime and renders the spec
    /// fetched from <c>/docs/asyncapi.json</c>. Fallback message shows
    /// if the CDN is unreachable (offline dev).
    /// </summary>
    public const string AsyncApiViewerHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>Briscola SignalR Hubs — AsyncAPI</title>
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <link rel="stylesheet" href="https://unpkg.com/@asyncapi/react-component@2/styles/default.min.css" />
          <style>
            body { margin: 0; font-family: ui-sans-serif, system-ui, sans-serif; background: #fafafa; color: #1a1a1a; }
            header { padding: 1rem 1.5rem; border-bottom: 1px solid #e4e4e7; background: #fff; }
            header a { color: #0b6bcb; text-decoration: none; margin-right: 1rem; font-size: 0.9rem; }
            header a:hover { text-decoration: underline; }
            #fallback { padding: 2rem 1.5rem; max-width: 720px; }
            code { background: #f4f4f5; padding: 0 0.25rem; border-radius: 3px; }
          </style>
        </head>
        <body>
          <header>
            <a href="/docs">← all docs</a>
            <a href="/docs/asyncapi.json">raw JSON</a>
            <a href="/scalar">REST (Scalar)</a>
            <a href="/swagger">REST (Swagger UI)</a>
          </header>
          <main>
            <div id="asyncapi"></div>
            <div id="fallback" hidden>
              <h2>Couldn't load the AsyncAPI viewer.</h2>
              <p>This page pulls <code>@asyncapi/react-component</code> from a CDN. If you're offline, browse the spec at <a href="/docs/asyncapi.json">/docs/asyncapi.json</a> or run it through any AsyncAPI-aware tool (Studio, CLI, generators).</p>
            </div>
          </main>
          <script>
            (function () {
              var fallback = document.getElementById('fallback');
              var target = document.getElementById('asyncapi');
              var script = document.createElement('script');
              script.src = 'https://unpkg.com/@asyncapi/react-component@2/browser/standalone/index.js';
              script.onerror = function () { fallback.hidden = false; };
              script.onload = function () {
                if (!window.AsyncApiStandalone) { fallback.hidden = false; return; }
                fetch('/docs/asyncapi.json')
                  .then(function (r) { return r.json(); })
                  .then(function (schema) {
                    window.AsyncApiStandalone.render({
                      schema: schema,
                      config: { show: { sidebar: true, errors: true } }
                    }, target);
                  })
                  .catch(function () { fallback.hidden = false; });
              };
              document.head.appendChild(script);
            })();
          </script>
        </body>
        </html>
        """;
}
