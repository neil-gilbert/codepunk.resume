# Web platform starting point

This fork keeps lucidRESUME's extraction, JobML, and projection libraries as its
upstream base. `src/lucidRESUME.Platform` is a separate ASP.NET Core host for the
candidate-owned web product. The current host deliberately exposes no career
records or upload endpoints.

## First useful flow

1. Candidate signs in and imports an existing CV.
2. Candidate reviews roles and individual claims before acceptance.
3. Candidate selects a claim and nominates a colleague at a company address.
4. The colleague uses a short-lived, single-use link to confirm, correct, decline,
   or say they cannot confirm the exact claim revision. No colleague account is
   required.
5. Candidate creates an immutable, job-specific CV and chooses which attestations
   appear on its recruiter link. Recruiter account is optional.

## Boundaries to implement before real candidate data

- Candidate identity and tenant ownership for every career record, source file,
  claim revision, attestation, and publication.
- Private source files in object storage; relational metadata and append-only
  decisions in PostgreSQL. The original JobML remains a portable export.
- An attestation refers to the stable JobML claim ID plus a hash of the exact
  statement and revision. A material edit requires a new attestation.
- A company-email challenge demonstrates mailbox control, not formal employer
  authorisation. Display the verifier's relationship and method plainly.
- The public application page exposes only the candidate-approved snapshot and
  selected attestations. The full career transcript is private by default.
- Upload processing, email invitations, rate limiting, withdrawal, retention,
  and audit logging are separate services or background jobs.

The upstream `lucidRESUME.Web` routes currently use a single file-backed
`current` career record and expose `/resume/api/jobml` and transcript routes.
Do not mount that endpoint group in the multi-user host until storage, reads,
and publication routes are scoped to the authenticated candidate or to an
explicitly shared snapshot.

## First implementation slice

Implement candidate sign-in and a tenant-scoped career-record import/review
flow using upstream ingestion and JobML libraries. After that, add the exact
claim attestation flow. Avoid introducing a recruiter database until the
shareable snapshot gets real use.

Run the host with `dotnet run --project src/lucidRESUME.Platform` on .NET 10.
This scaffold has only a landing page and `/health`; it does not accept data.
