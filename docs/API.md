# API contract

Same-origin JSON over HTTP for local development, HTTPS in deployment. Every POST needs `X-ReasoningLab: 1`; cross-origin requests are rejected. `POST /api/profile` establishes an HttpOnly, SameSite=Strict protected cookie. Every learner endpoint validates that cookie and scopes database access to its learner. Answer keys and explanations never appear in question DTOs.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/health` | Process health (not database readiness) |
| POST | `/api/profile` | Create or recover this browser's anonymous profile |
| GET | `/api/catalog` | Skill graph, pathways and reference blueprints |
| GET | `/api/blueprints/{module}?on=2027-08-01` | Select a dated reference blueprint (end date exclusive) |
| POST | `/api/sessions` | `{ "pathway": "general", "mode": "practice" }`; resumes an unfinished matching session |
| GET | `/api/sessions/{id}` | Current question, progress and server deadline |
| POST | `/api/sessions/{id}/answers` | `{ "questionId": "UUID", "choice": 0 }`; validates assignment, persists attempt/evidence atomically |
| GET | `/api/dashboard` | Shared skill evidence, pathway practice, raw diagnostic results and recommendations |
| POST | `/api/admin/questions` | Append immutable question version, requiring `X-Admin-Token` |

Supported pathways are `general`, `lsat`, `ucat`. Supported modes are `practice` and `diagnostic`; only LSAT currently has the original short diagnostic. No full mock endpoint exists. Diagnostics pin version 1 of their configured questions and do not adapt to mastery. Deadline is authoritative on the server. Late submissions finalize the result, counting unanswered items as incorrect. Reload and dashboard access also finalize expired diagnostics; no background scheduler is needed.

The question DTO's `id` identifies a specific assignment, not its reusable question version. Repeated identical submissions are idempotent, including delayed retries after a later spaced-review assignment. A conflicting resubmission returns 409. Sessions, attempts, evidence and mastery changes share a transaction; PostgreSQL transaction-scoped learner locks serialize concurrent writes across app instances. Questions already seen in a diagnostic are excluded from fresh practice selection. Future high-stakes diagnostics need separate exposure policies and larger banks.

Content studio currently accepts one skill mapping per question. The relational model supports multiple mappings; richer authoring and weighted evidence policies are next-step work. Read-only catalog exposes relationships. Prerequisite graph editing and cycle validation are not implemented.

There is no production identity, rate limiting, role/audit administration or commercial entitlement layer yet. Treat the initial application as a local prototype. Error responses for expected rule failures use `{ "error": "message" }` with 400/401/403/404/409 statuses. ASP.NET handles malformed JSON and type binding as 400.
