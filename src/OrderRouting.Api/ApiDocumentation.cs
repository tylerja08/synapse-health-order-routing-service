using System.Text.Json;

namespace OrderRouting.Api;

public static class ApiDocumentation
{
    public static IResult OpenApiJson()
    {
        return Results.Json(OpenApiDocument);
    }

    public static IResult SwaggerPage()
    {
        return Results.Content(SwaggerHtml, "text/html; charset=utf-8");
    }

    private static readonly object OpenApiDocument = new
    {
        openapi = "3.0.3",
        info = new
        {
            title = "Order Routing Service API",
            version = "1.0.0",
            description = "Routes orders to eligible suppliers using product category, ZIP coverage, mail-order eligibility, priority scheduling, and supplier quality rules."
        },
        paths = new Dictionary<string, object>
        {
            ["/health"] = new
            {
                get = new
                {
                    summary = "Health check",
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Service is running",
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    schema = new Dictionary<string, object>
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new Dictionary<string, object>
                                        {
                                            ["status"] = new { type = "string", example = "ok" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            ["/api/route"] = new
            {
                post = new
                {
                    summary = "Route an order",
                    description = "Returns HTTP 200 for parsed business requests. Use feasible=false with errors for validation or routing failures. Malformed JSON returns HTTP 400.",
                    requestBody = new
                    {
                        required = true,
                        content = new Dictionary<string, object>
                        {
                            ["application/json"] = new
                            {
                                schema = new
                                {
                                    type = "object",
                                    required = new[] { "customer_zip", "mail_order", "items" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["order_id"] = new { type = "string", example = "ORD-EXAMPLE" },
                                        ["customer_zip"] = new { type = "string", pattern = "^[0-9]{5}$", example = "10015" },
                                        ["mail_order"] = new { type = "boolean", example = false },
                                        ["priority"] = new { type = "string", @enum = new[] { "rush", "standard" }, example = "standard" },
                                        ["items"] = new
                                        {
                                            type = "array",
                                            minItems = 1,
                                            items = new
                                            {
                                                type = "object",
                                                required = new[] { "product_code", "quantity" },
                                                properties = new Dictionary<string, object>
                                                {
                                                    ["product_code"] = new { type = "string", example = "WC-STD-001" },
                                                    ["quantity"] = new { type = "integer", minimum = 1, example = 1 }
                                                }
                                            }
                                        }
                                    }
                                },
                                example = new
                                {
                                    order_id = "ORD-EXAMPLE",
                                    customer_zip = "10015",
                                    mail_order = false,
                                    priority = "standard",
                                    items = new[]
                                    {
                                        new { product_code = "WC-STD-001", quantity = 1 },
                                        new { product_code = "OX-PORT-024", quantity = 1 }
                                    }
                                }
                            }
                        }
                    },
                    responses = new Dictionary<string, object>
                    {
                        ["200"] = new
                        {
                            description = "Routing result or business validation failure",
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new
                                {
                                    examples = new Dictionary<string, object>
                                    {
                                        ["success"] = new
                                        {
                                            value = new
                                            {
                                                feasible = true,
                                                routing = new[]
                                                {
                                                    new
                                                    {
                                                        supplier_id = "SUP-005",
                                                        supplier_name = "Respiratory Care Co Co",
                                                        items = new[]
                                                        {
                                                            new
                                                            {
                                                                product_code = "WC-STD-001",
                                                                quantity = 1,
                                                                category = "wheelchair",
                                                                fulfillment_mode = "local"
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        },
                                        ["infeasible"] = new
                                        {
                                            value = new
                                            {
                                                feasible = false,
                                                errors = new[] { "priority must be one of: rush, standard." }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        ["400"] = new { description = "Malformed JSON" }
                    }
                }
            }
        }
    };

    private static readonly string SwaggerHtml = $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Order Routing Service API</title>
          <style>
            :root {
              color-scheme: light;
              --bg: #f6f8fa;
              --panel: #ffffff;
              --ink: #1f2937;
              --muted: #5b677a;
              --border: #d7dde5;
              --accent: #1f6feb;
              --accent-dark: #1756b5;
              --success: #116329;
              --code: #0f172a;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              background: var(--bg);
              color: var(--ink);
              font-family: "Segoe UI", system-ui, -apple-system, sans-serif;
              line-height: 1.45;
            }
            header {
              background: #152238;
              color: white;
              padding: 22px clamp(16px, 4vw, 48px);
            }
            header h1 {
              margin: 0 0 6px;
              font-size: 24px;
              letter-spacing: 0;
            }
            header p { margin: 0; color: #d7e1ef; }
            main {
              width: min(1120px, calc(100vw - 32px));
              margin: 24px auto 48px;
              display: grid;
              gap: 18px;
            }
            section {
              background: var(--panel);
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 18px;
            }
            h2 {
              margin: 0 0 12px;
              font-size: 18px;
            }
            .endpoint {
              display: flex;
              align-items: center;
              gap: 10px;
              flex-wrap: wrap;
              margin-bottom: 12px;
            }
            .method {
              background: var(--success);
              color: white;
              border-radius: 4px;
              padding: 4px 8px;
              font-weight: 700;
              font-size: 13px;
            }
            code, pre, textarea {
              font-family: Consolas, "Liberation Mono", monospace;
            }
            .path {
              font-weight: 700;
              color: var(--code);
            }
            .grid {
              display: grid;
              grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
              gap: 16px;
            }
            label {
              display: block;
              font-weight: 700;
              margin-bottom: 8px;
            }
            textarea, pre {
              width: 100%;
              min-height: 360px;
              border: 1px solid var(--border);
              border-radius: 6px;
              padding: 12px;
              background: #fbfcfe;
              color: var(--code);
              font-size: 13px;
              overflow: auto;
              white-space: pre-wrap;
            }
            button, a.button {
              appearance: none;
              border: 0;
              background: var(--accent);
              color: white;
              border-radius: 6px;
              padding: 9px 13px;
              font-weight: 700;
              cursor: pointer;
              text-decoration: none;
              display: inline-flex;
              align-items: center;
              justify-content: center;
              min-height: 38px;
            }
            button:hover, a.button:hover { background: var(--accent-dark); }
            .actions {
              display: flex;
              gap: 10px;
              flex-wrap: wrap;
              margin: 12px 0 0;
            }
            .muted { color: var(--muted); }
            .status {
              margin-top: 10px;
              min-height: 22px;
              color: var(--muted);
              font-size: 14px;
            }
            @media (max-width: 820px) {
              .grid { grid-template-columns: 1fr; }
              textarea, pre { min-height: 280px; }
            }
          </style>
        </head>
        <body>
          <header>
            <h1>Order Routing Service API</h1>
            <p>Interactive local API documentation for routing orders to suppliers.</p>
          </header>
          <main>
            <section>
              <h2>Endpoints</h2>
              <div class="endpoint"><span class="method">GET</span><span class="path">/health</span><span class="muted">Service health check</span></div>
              <div class="endpoint"><span class="method">POST</span><span class="path">/api/route</span><span class="muted">Route an order</span></div>
              <div class="actions">
                <a class="button" href="/openapi.json" target="_blank" rel="noreferrer">Open OpenAPI JSON</a>
                <button id="healthButton" type="button">Try Health Check</button>
              </div>
              <div id="healthStatus" class="status"></div>
            </section>

            <section>
              <h2>Try POST /api/route</h2>
              <div class="grid">
                <div>
                  <label for="requestBody">Request body</label>
                  <textarea id="requestBody" spellcheck="false">{
          "order_id": "ORD-EXAMPLE",
          "customer_zip": "10015",
          "mail_order": false,
          "priority": "standard",
          "items": [
            { "product_code": "WC-STD-001", "quantity": 1 },
            { "product_code": "OX-PORT-024", "quantity": 1 }
          ]
        }</textarea>
                  <div class="actions">
                    <button id="sendButton" type="button">Execute</button>
                    <button id="resetButton" type="button">Reset Example</button>
                  </div>
                </div>
                <div>
                  <label for="responseBody">Response</label>
                  <pre id="responseBody">Run the request to see the response.</pre>
                </div>
              </div>
              <div id="routeStatus" class="status"></div>
            </section>
          </main>
          <script>
            const example = document.getElementById('requestBody').value;
            const responseBody = document.getElementById('responseBody');
            const routeStatus = document.getElementById('routeStatus');
            const healthStatus = document.getElementById('healthStatus');

            document.getElementById('resetButton').addEventListener('click', () => {
              document.getElementById('requestBody').value = example;
              responseBody.textContent = 'Run the request to see the response.';
              routeStatus.textContent = '';
            });

            document.getElementById('healthButton').addEventListener('click', async () => {
              healthStatus.textContent = 'Calling /health...';
              try {
                const response = await fetch('/health');
                const body = await response.text();
                healthStatus.textContent = `HTTP ${response.status}: ${body}`;
              } catch (error) {
                healthStatus.textContent = error.message;
              }
            });

            document.getElementById('sendButton').addEventListener('click', async () => {
              routeStatus.textContent = 'Calling /api/route...';
              responseBody.textContent = '';
              try {
                const parsed = JSON.parse(document.getElementById('requestBody').value);
                const response = await fetch('/api/route', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify(parsed)
                });
                const text = await response.text();
                routeStatus.textContent = `HTTP ${response.status}`;
                try {
                  responseBody.textContent = JSON.stringify(JSON.parse(text), null, 2);
                } catch {
                  responseBody.textContent = text;
                }
              } catch (error) {
                routeStatus.textContent = 'Request was not sent.';
                responseBody.textContent = error.message;
              }
            });
          </script>
        </body>
        </html>
        """;

    public static string OpenApiJsonText => JsonSerializer.Serialize(OpenApiDocument, new JsonSerializerOptions { WriteIndented = true });
}
