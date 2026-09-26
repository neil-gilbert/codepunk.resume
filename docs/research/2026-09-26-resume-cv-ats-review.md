# Resume, CV and ATS compatibility review

**Date:** 26 September 2026  
**Status:** Research, implementation and post-change verification  
**Scope:** lucidRESUME role-specific Markdown, DOCX and PDF projections, including optional cJobML

## Executive finding

lucidRESUME now emits conservative, selectable-text DOCX and tagged PDF/UA output
with one visual reading order. The candidate identity appears only on page one,
achievements are real Word and PDF list items, and role headers expose title,
employer, optional location and dates in a stable order. In the post-change fixture,
the independent plain-text parser found all six role boundaries in both formats and
paired five exactly. Its first title was split at an internal comma.

That is strong compatibility evidence, not a universal ATS guarantee. OpenResume
recovers the content but still groups several PDF roles because its public parser
detects repeated records using coordinate-gap heuristics. It also treats the
unfamiliar cJobML References section as Projects. Both behaviors remain useful
regression signals, but neither justifies weakening or hiding the evidence layer.

The right goal is not an invented universal "ATS score". It is a conformance suite
which measures four independent properties:

1. exact text and link survival;
2. correct reading and section order;
3. correct classification of people, employers, roles, dates, education and skills;
4. preservation of evidence without corrupting ordinary résumé fields.

## What was tested

The baseline artifact was the two-page Engineering Lead candidate generated on
25 September 2026:

```text
/Users/scottgalloway/Desktop/resumes/generated/
  genai-roles-2026-09-25/quality-loop/final-candidates/
  scott-galloway-engineering-lead.{pdf,docx}
```

It was tested with:

- the actual OpenResume browser parser, running locally from its current source;
- `resume-parser-ats`, which packages the OpenResume algorithm as a CLI;
- ATS Reader, using PDFPlumber for PDF and python-docx for Word;
- inspection of the emitted Open XML and the QuestPDF implementation.

OpenResume is a useful transparent regression target. It is deliberately limited to
single-column English PDFs and uses hand-written feature scoring. It is not a model of
all commercial ATS products and cannot certify compatibility. ATS Reader is also an
advisory open-source implementation, not a commercial parser. Agreement between the
two is nevertheless useful evidence of structural defects in our output.

## Verified original baseline results

| Property | PDF | DOCX | Assessment |
|---|---:|---:|---|
| Selectable text | Pass | Pass | Core prose survives. |
| Single visual reading order | Pass | Pass | No sidebar or visual multi-column résumé. |
| Name and email | Pass | Pass | OpenResume recovered both from PDF. |
| International phone | Partial | Text survives | OpenResume truncated the UK number to ten digits; the raw source is correct. |
| Location and web links | Pass | Pass | Raw values survive. |
| Conventional sections | Pass | Pass | Summary, Skills, Experience, Education and Projects are visible. |
| Work title/employer pairing | Fail | Partial | Combined `Title — Employer` lines are merged, duplicated or shifted. |
| Date pairing | Partial | Partial | Dates survive, but simpler parsers associate some with the next role. |
| Education classification | Fail in OpenResume | Partial | Text survives; OpenResume left structured education empty. |
| Repeated header safety | Fail | Pass | PDF page-two identity block became a fake employer. |
| Semantic bullets | Not tagged | Fail | DOCX types a bullet glyph instead of using Word numbering/list semantics. |
| Tagged semantic structure | Fail | N/A | PDF has no structure tree/PDF-UA tags. |
| cJobML content survival | Pass | Pass | Markers, references and URLs remain in extracted text. |
| cJobML field isolation | Fail in OpenResume | Unverified broadly | `References` is classified as a Project. |

Two parser-specific observations must not be confused with exporter defects:

- OpenResume's public phone regular expression is US-centric, so its truncation of a
  valid UK mobile number is primarily an OpenResume limitation. Our regression suite
  still needs international fixtures to expose parsers with that limitation.
- OpenResume has no References field. Classifying cJobML as Projects proves that the
  text survived, not that every commercial ATS will make the same choice.

## Post-change verification

The same Engineering Lead source was projected again after the exporter changes and
checked in both tools. The resulting PDF was also rendered page by page and inspected.

| Property | PDF | DOCX |
|---|---:|---:|
| Candidate identity repeated on later pages | No | No |
| Page counter in extracted text | No | No |
| Tagged semantic structure | PDF/UA-1 structure tree | Native Word structure |
| True semantic lists | Pass | Pass |
| Tables, columns, images or text boxes | None | None |
| Plain-text parser role count | 6 of 6 | 6 of 6 |
| Exact title/employer pairing | 5 of 6 | 5 of 6 |
| Name, email, phone and location text | Pass | Pass |
| Conventional section recognition | Pass | Pass |
| Open XML validation | N/A | Pass |
| cJobML markers and references survive | Pass | Pass |

OpenResume now recovers the complete UK phone number, education, all role header text,
all prose and all links. It still emits one combined work record for several roles.
Its subsection algorithm uses the most common visual line gap as a threshold, so this
is a known limitation of that particular open parser rather than lost content.

ATS Reader recovers six distinct roles in each output. It splits `Founder, Owner and
Principal Consultant` at the first comma and treats the remainder as the employer;
the other five title/employer pairs are exact. Its US-oriented phone regular
expression also truncates the same UK mobile despite the raw text remaining correct. It
also warns that the top content on PDF page two may be a header even though the PDF
contains no header object and the content is not repeated. The warning is therefore a
false positive from its page-position heuristic.

The checked files were generated at:

```text
/private/tmp/lucidresume-ats-final/
  scott-galloway-engineering-lead.{md,docx,pdf}
```

## Immediate changes

### P0: make the PDF structurally safe

1. Move the name and contact block out of `page.Header()` and into the first page's
   body content. Do not repeat it on subsequent pages.
2. Remove page counters from the ATS Classic output, or mark them as semantic
   artifacts and prove they do not appear in extracted résumé text.
3. Enable QuestPDF's PDF/UA conformance and add semantic document, heading,
   paragraph, list, list-item and link tags. QuestPDF 2026.9.0, already referenced by
   the project, exposes these APIs.
4. Validate the result with veraPDF as well as text extraction. PDF/UA is an
   accessibility standard, not an ATS standard, but its explicit reading order and
   semantics remove exactly the ambiguity document parsers currently face.

The PDF Association explains that tagged PDF supplies intended reading order and
semantic types such as headings and lists. QuestPDF documents both PDF/UA conformance
and semantic helpers:

- https://pdfa.org/resource/tagged-pdf-q-a/
- https://www.questpdf.com/concepts/accessibility.html
- https://www.questpdf.com/concepts/document-settings.html

### P0: make field boundaries explicit

Every work item should expose fields in this logical order:

```text
Role title
Employer
Location
Jan 2024 - Present
Achievement bullet
Achievement bullet
```

The tested exporter uses a conventional `Role | Employer | Location` identity followed
by a separately positioned date. In plain-text extraction the same values remain on
one delimited line, which allowed the format-neutral parser to recover all six roles.
In tagged PDF the styled spans remain distinct semantic text items. This performed
better than either an undelimited em-dash label or four unrelated paragraphs in the
tested parsers.

Apply the same rule to education:

```text
University of Stirling
BSc (Hons), Psychology
Graduation date, when known
```

Contact values should also be distinct text runs or paragraphs with exact labels or
unambiguous URI schemes. A parser should never receive email, phone, location and five
URLs as one candidate field.

### P0: remove structural pseudo-entries

`Selected client engagements through Mostlylucid Ltd` currently appears between work
items. OpenResume interprets it as an employer. Keep it as ordinary explanatory prose
outside the repeated experience-item pattern, or omit it and put `via Mostlylucid Ltd`
inside each client engagement.

Overlapping consultancy, founder and client dates are legitimate. Flattening each
engagement into a complete experience record is safer than nesting records visually.

### P0: emit real Word lists

The DOCX exporter currently writes `•` into a paragraph. Add a NumberingDefinitionsPart
and NumberingProperties so achievements are real list paragraphs. Preserve a readable
bullet in plain-text extraction, but make the Open XML semantics explicit. Microsoft
documents numbering as the WordprocessingML mechanism for list labels:

https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.numbering

### P1: define two explicit publication profiles

The existing optional cJobML behavior should become a named, tested choice rather than
an accidental variation:

- **Evidence-linked résumé:** human résumé plus inline citations, compact References
  and the full JobML endpoint. This is the lucidRESUME default and preserves the core
  product thesis.
- **Strict ATS résumé:** the same human projection, with conservative formatting and a
  deliberately selected cJobML mode: full compact references, endpoint-only, or off.
  The user chooses this only for a portal demonstrated to mishandle the evidence
  section.

Never hide JobML in white text, tiny text, off-page objects or keyword stuffing. If
the visible compact section is omitted, the exported projection manifest and full
JobML endpoint still exist outside the submitted document, but the résumé must not
pretend that an ATS necessarily followed that endpoint.

Changing `References` to another unfamiliar heading is not a demonstrated fix.
OpenResume lacks a references/evidence category, so any non-core section can fall back
to Projects. Test `Evidence and References`, `Publications and Evidence`, and an
endpoint-only machine-area line against the corpus, then choose based on measured
field contamination and cold-parser comprehensibility.

### P1: offer DOCX as the conservative submission recommendation

Greenhouse accepts DOC, DOCX, PDF, RTF and TXT, while Workday HiredScore lists the same
five formats. Greenhouse also explicitly warns about columns, tables, graphics, text
boxes and critical information in headers and footers. Until the tagged-PDF work is
complete and validated, the UI should recommend DOCX for unknown ATS portals and PDF
when fidelity or a portal requirement makes PDF preferable.

- https://support.greenhouse.io/hc/en-us/articles/360052218132-Supported-formats-for-resumes-cover-letters-and-other-candidate-uploads
- https://support.greenhouse.io/hc/en-us/articles/200989175-Unsuccessful-resume-parse
- https://doc.workday.com/hiredscore/en-us/workday-hiredscore/recruiter-productivity-/concept--candidate-profiles.html
- https://careers.roche.com/global/en/resume-parsing-faq

## Automated ATS lab

Add a repository-owned test harness rather than relying on manual uploads or a single
third-party score.

### Test adapters

1. **Native extraction**
   - Open XML body order, styles, numbering, hyperlinks and bookmarks;
   - PDF text items, reading order, hyperlinks and structure tree;
   - Apache Tika as a format-neutral extraction baseline.
2. **OpenResume**
   - browser adapter for PDF;
   - pin a reviewed commit because its dependency stack and heuristics can change.
3. **ATS Reader**
   - independent PDF and DOCX structural warnings and field extraction;
   - pin a reviewed commit and treat results as advisory.
4. **Accessibility/conformance**
   - veraPDF for PDF/UA;
   - Open XML SDK validation for DOCX.
5. **Office round trips**
   - Microsoft Word on macOS, because it is installed on the development machine;
   - LibreOffice in CI when available;
   - re-extract after save/export to catch altered reading order or broken links.
6. **JobML checks**
   - deterministic cJobML parser;
   - cold-parser questions with no JobML-specific prompt;
   - exact marker-to-reference and reference-to-endpoint validation.

### Golden fixtures

The corpus should contain at least:

- UK, US and EU phone/address formats;
- one-page and two-page résumés;
- simultaneous consultancy/client roles;
- month-year, year-only and ongoing dates;
- projects, publications, qualifications and certifications;
- ASCII and ordinary Unicode punctuation;
- long technical skills such as `ASP.NET Core`, `C#`, `CI/CD` and `Node.js`;
- cJobML off, endpoint-only and full compact references;
- a deliberately broken two-column control fixture so the tests prove they can fail.

Every fixture needs an expected manifest independent of the rendered document. It
should identify exact contacts, ordered sections, employers, roles, dates, bullets,
skills, citations and URLs.

### Metrics and release gates

| Metric | Proposed release gate |
|---|---:|
| Source text retained | 100% of meaningful text blocks |
| Name/email/phone exact | 100% on native extractors; parser results reported separately |
| Section order | Exact match |
| Experience count | Exact match |
| Employer-role-date association | Exact match on native semantics; >= 95% across open parser matrix |
| Education association | Exact match |
| Achievement bullet count/order | Exact match |
| URLs and internal citations | Exact match |
| Critical data in header/footer | Zero |
| PDF semantic structure | Tagged and veraPDF-validated |
| Core-field contamination by cJobML | Zero in native manifest; explicitly measured per third-party parser |

The report should show parser-by-parser results. A weighted aggregate may be useful in
the UI, but it must never be labelled a universal ATS pass probability.

## Content and formatting review

### Findings supported by research

- A controlled study of 90 participants found formal résumé designs were preferred
  over creative layouts for otherwise equivalent candidates. This supports the
  restrained templates, but does not prove one ideal design:
  https://doi.org/10.1080/13594320902903613
- A 2025 study following 183 job seekers associated clearer, more detailed and better
  structured application writing with more interviews and shorter searches. It also
  cautions that surface composition can become an easily generated signal:
  https://doi.org/10.1111/ijsa.70022
- MIT recommends concise accomplishment statements with project/context, action and
  result, with scale or outcomes quantified where possible:
  https://capd.mit.edu/resources/resumes-writing-about-your-skills/

### What this means for lucidRESUME

1. Keep reverse chronology and conventional names: Summary, Skills, Experience,
   Education, Certifications, Projects and Publications.
2. Keep a single logical reading order. Restrained colour and typography are fine;
   colour must not carry meaning.
3. Prefer two pages for a highly experienced technical leader when the second page
   contains relevant evidence. Do not enforce a one-page folklore rule.
4. Keep bullets short enough to scan, but optimize for one complete accomplishment,
   not an arbitrary word count. The current output often puts several mechanisms and
   outcomes into one long bullet; the compiler should select or split reviewed source
   passages without inventing metrics.
5. Show skills twice for different purposes: a compact searchable inventory and
   evidence-bearing use within experience/project prose. A bare skill remains weaker
   than an evidenced claim.
6. Exact job-description terminology should select and order accepted concepts and
   aliases. It must not add unsupported terms. Matching is retrieval from the ledger,
   not keyword manufacture.
7. Maintain locale-aware spelling, phone/date formats and résumé/CV conventions.
   Parser tests must not silently normalize valid UK data to US expectations.

## Standards landscape

There is no universal standard that an uploaded Word or PDF résumé can implement to
guarantee correct parsing by every ATS. Several standards solve adjacent parts of the
problem:

| Standard/ecosystem | Useful part | Gap relative to JobML |
|---|---|---|
| HR Open Standards | Enterprise HR/recruiting data exchange | Not a universally consumed résumé-upload payload; no document-local evidence contract. |
| JSON Resume | Open, permissive JSON schema for common résumé fields | Good projection/import target; limited claim provenance and live prose linkage. |
| Europass CV data model | European CV interoperability and multilingual controlled data | Primarily profile/CV exchange; evidence graph and reversible prose editing are not its central model. |
| schema.org Person/Occupation | Web discovery for identity, roles, credentials and skills | Sparse history/evidence semantics; best as endpoint JSON-LD. |
| ISO 29500 / WordprocessingML | Semantic DOCX paragraphs, headings, numbering and links | File structure, not recruiting semantics. |
| ISO 32000 + PDF/UA | Machine-readable text, reading order and semantic PDF structure | File structure and accessibility, not recruiting semantics. |

Relevant primary entry points:

- https://www.hropenstandards.org/
- https://jsonresume.org/schema
- https://europass.europa.eu/system/files/2020-08/ECV_Schema_Documentation_v3.0.0_20200602.pdf
- https://schema.org/Person
- https://schema.org/Occupation
- https://learn.microsoft.com/en-us/office/open-xml/word/structure-of-a-wordprocessingml-document

lucidRESUME should keep JobML as its canonical evidence-aware interchange, while
maintaining tested adapters:

- continue JSON Resume export and add round-trip field tests;
- consider an HR Open recruiting/profile adapter once the required public schema and
  use case are pinned;
- import/export Europass where it materially helps European users;
- publish schema.org JSON-LD beside the full JobML endpoint;
- use native DOCX and tagged-PDF semantics instead of expecting YAML alone to repair a
  visually ambiguous document.

The emerging `.cv` project is worth watching because it packages PDF/A-3, Markdown,
JSON Resume and embeddings in one container. Its embedded-payload idea is close to the
two-resolution goal, but normal ATS upload paths are not demonstrated to inspect PDF
attachments. It should be benchmarked, not adopted as a dependency on its claims
alone: https://github.com/cvfile/cv

## Product conclusions

The current architecture is directionally correct: one reviewed career ledger,
human-owned prose, deterministic projections, explicit evidence and ordinary
DOCX/PDF deliverables. ATS compatibility does not require weakening that thesis.

It does require making the conventional résumé layer exceptionally boring and
explicit at the file-structure level. Human prose can remain human. cJobML can remain
high-resolution. The bridge between them must use headings, lists, blocks, links and
reading order that ordinary parsers can recover without guessing.

The first four structural changes are implemented and covered by exporter tests. The
next implementation slice is to add a checked-in golden ATS corpus and pinned external
parser adapters, then compare wording, layouts and cJobML publication modes using
measured regressions.
