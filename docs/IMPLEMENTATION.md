# ReasoningLab implementation plan

## Specification review and scope

ReasoningLab is one reasoning platform. General, LSAT and UCAT pathways reuse a skill graph and learner evidence; examination readiness is a separate construct. Adaptive practice changes question selection, while assessment forms pin questions, versions, timing and scoring. New skill levels are data, not schema changes. The complete curriculum and validated exam simulations cannot be delivered or verified in a first increment.

## Architecture decision

Use .NET 10 throughout: a standalone Blazor WebAssembly client served by ASP.NET Core, a small shared Core library for domain policy and API contracts, and EF Core with PostgreSQL. Server folders separate HTTP endpoints, application services and persistence. No message bus, microservices, generic repository, CQRS framework or separate frontend deployment is needed. The database is the source of truth; answers and explanations stay on the server until feedback is permitted. The client never calculates authoritative scores or deadlines.

Core has no EF or HTTP dependency. Client references Core contracts; Server references Core and hosts Client assets. EF migrations version the relational schema. Transactions and per-learner PostgreSQL advisory locks protect attempts, evidence and mastery from concurrent requests. API sessions are scoped to an opaque, protected anonymous learner cookie. This is a prototype identity, not a production account system.

## Increment 1 — working vertical slice

- Original deductive reasoning questions across general reasoning, LSAT-style and UCAT-style practice.
- Persistent learner identity, versioned content, skill mappings, prerequisites and pathways.
- Explainable provisional mastery: three distinct correct items at the current level unlock the next level; review repetitions do not inflate advancement.
- Immediate explanations, misconception records and due review scheduling.
- Shared dashboard with domain practice evidence and separate raw diagnostic results.
- A fixed three-question, three-minute original LSAT-style diagnostic with a server deadline and no feedback during the assessment. This is **not** a full LSAT mock.
- Dated examination blueprint metadata and a minimal administrator question-version editor.
- Domain and HTTP integration tests against real PostgreSQL, responsive browser checks, build/publish validation and GitHub Actions.

## Subsequent increments and acceptance gates

1. Curriculum/content tooling: prerequisite editing and cycle validation, multi-skill authoring, reviewed content publication, passages, reusable question renderers and a substantial progressively difficult bank. Gate: content review and progression tests.
2. Exam modules: finish official blueprint verification, add fixed section navigation, breaks, permitted tools and accommodations, then full mocks. Gate: independently audited counts, timing, scoring and end-to-end exam scenarios. Never enable incomplete blueprints.
3. Production identity: account recovery, multi-device persistence, roles, consent/retention, audit trail, abuse limits, accessible authentication. Gate: security/privacy review and tenant isolation tests.
4. Learning science: calibrate difficulty/mastery, validate evidence thresholds, improve spaced repetition and misconception feedback. Gate: expert review and measured learner outcomes. No unvalidated score predictions.
5. Commercial release: payments/entitlements if required, deployment/backups, observability and recovery testing; achievements and opt-in leaderboards after privacy/competition policies are agreed.

## Decisions requiring product input before those increments

- Intended learner ages, markets (UK UCAT versus UCAT ANZ), accessibility accommodations and privacy jurisdiction.
- Content authorship/licensing and subject-matter review ownership; permitted use of exam branding.
- Account model, paid plans, administration roles, data retention and deletion policy.
- Mastery validation, readiness reporting and whether any score prediction is wanted and defensible.
- Hosting target, uptime requirements, analytics and leaderboard opt-in rules.

Provisional engineering defaults are documented, reversible and do not resolve those business decisions. No public deployment or merge is part of this initial implementation.

## Requirement coverage

Skills/prerequisites, curricula/pathways, modules/blueprints, banks/question versions/mappings, learner mastery/evidence, practice sessions/attempts and diagnostic results are the first-slice persistence model. Full curriculum delivery, full mocks, complete relationship administration, achievements and leaderboards remain planned. A short diagnostic demonstrates separation of practice from fixed assessment; it does not satisfy full exam simulation acceptance.
