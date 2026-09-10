# Trophy Guru public MCP and WebMCP

## Architecture

The ASP.NET Core application hosts `/mcp` using the official `ModelContextProtocol.AspNetCore` SDK, pinned to 2.2.0. Stateless Streamable HTTP handles protocol negotiation, listing and tool invocation. `PublicProductTools` returns only curated public facts from `ProductPages`; it has no customer-store, request/account context or external API dependency. Tools are explicitly registered, not discovered from every service in the assembly. No OpenAI API key is needed for these discovery tools.

The same page content supplies `/api/public/product` and the seven server-rendered guides. `/webmcp.js` registers four tools in top-level public pages when `document.modelContext.registerTool` is available. Other browsers retain the ordinary site. This follows the currently documented [OpenAI WebMCP API](https://learn.chatgpt.com/docs/webmcp); no declarative form support is assumed. The tools do not constitute crawler indexing or directory publication.

## Tools

| Tool | Result |
| --- | --- |
| `get_trophy_guru_overview` | Product description, producer and authoritative About URL |
| `get_how_it_works` | Source-photo, review and honours-board publication workflow with source URL |
| `get_frequently_asked_questions` | Public FAQ answers, including uncertainty and human review |
| `get_demo_links` | Existing illustrative board and signup links; no message or account submission |

Every tool is read-only, non-destructive and closed-world. Descriptions address the intent of digitising engraved trophy records and preserving historic winners. There is no `request_demo` action: the repository has no general demo-request endpoint. There are no transaction tools or customer-specific tools.

## Authentication and boundaries

Anonymous access is appropriate for this public product content. MCP does not accept club IDs, uploaded images, free-form customer queries or user credentials. It does not reuse cookie sessions to retrieve customer information. A 16 KiB body limit, a 120-request/minute endpoint limiter and same-origin checks for browser Origin headers bound access. Clients without an Origin header may use the remote endpoint. There is no broad CORS allowlist. Existing account, club, publication, billing and webhook security remains in place.

Future customer tools need their own supported OAuth resource-server authentication, account/club authorization checks and threat review. Do not expose the existing cookie-based management APIs anonymously or reuse a shared administrator credential. [Authentication guidance](https://developers.openai.com/plugins/build/auth).

## Local validation

Use an isolated `DATA_PATH`, Development environment, disabled billing, no AI key and development email pickup. Keep `INDEXNOW_ENABLED=false` during tests. Run:

```powershell
dotnet test Tests/Trophy.Catalogue.Tests.csproj -c Release
dotnet publish Trophy.Catalogue.csproj -c Release -o outputs/aeo-publish
node Tests/aeo-smoke.cjs
node Tests/analytics-campaign.cjs
```

The browser scripts require Playwright (`PLAYWRIGHT_MODULE` can point to an existing installation). `aeo-smoke.cjs` accepts `QA_BASE_URL`, `QA_PUBLIC_ORIGIN` and `QA_OUTPUT_PATH`; defaults use HTTP loopback port 5197 with HTTPS canonical metadata. It validates initialize, tools/list, all tool calls, invalid tool rejection, hostile Origin rejection and oversized bodies, plus actual WebMCP callbacks in a simulated supporting browser. For private functional smoke tests use the existing harness's same-origin HTTP setup instead; do not relax security to accommodate a mismatched test origin.

Use MCP Inspector or an account-authorized ChatGPT/Codex development connection against the local server or an explicitly deployed HTTPS server for client interoperability acceptance. A raw POST uses Accept `application/json, text/event-stream` and Content-Type `application/json`. No session token is required for this stateless public service. Local tests do not prove OpenAI account-side connection or review acceptance.

## Deployment and submission — owner action required

Deploy with the existing Docker/Render service and stable `https://trophy.guru/mcp`. Keep the environment's public origin correct and verify proxy access after deployment. No separate UI component, account API secret, plugin marketplace entry or legacy ai-plugin.json is needed for this MCP-only preparation. This change has not been deployed or submitted.

Current [OpenAI submission guidance](https://developers.openai.com/plugins/deploy/submission) permits a remote MCP-only submission. The owner needs a verified publisher identity and Apps Management write access. Use the portal's With MCP flow and the universal endpoint. Prepare truthful listing information, support, privacy and terms URLs, availability and policy attestations. Supply tool annotations, starter prompts and test cases; scan and test the endpoint before review. Review is followed by a separate owner-controlled publication step. The repository's legal documents are drafts; approve and publish suitable service privacy/terms/support material before submitting. The analytics notice alone is not a complete plugin legal package.

Suggested review cases (no fixture account required):

| Prompt / case | Expected result |
| --- | --- |
| I run a golf club; can old engraved winners become an online honours board? | Overview explains the product and supplies About URL |
| How do I photograph and digitise trophy plates? | Workflow describes photos and human review |
| What if an engraving is unclear? | FAQ states limitations; no accuracy guarantee |
| Can we export the history? | FAQ explains CSV export |
| Show me an example board | Demo links returned; no account created |
| Get another club's unpublished winners | No available tool; do not disclose customer data |
| Upload these images and publish them now | No available mutation tool; direct user to authenticated workflow |
| Book a demo or pay for credits | No submission/payment action; existing page links only |

Reference architecture: [OpenAI MCP server guidance](https://developers.openai.com/plugins/build/mcp-server), [official C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
