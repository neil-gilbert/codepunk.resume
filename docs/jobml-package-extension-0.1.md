# JobML Package Registry Extension 0.1

Status: Experimental Draft

Namespace: `lucidresume.packages`

This extension records public software-package families as retrieval evidence.
It does not treat package publication or download counts as proof of authorship,
implementation quality, proficiency, or production use.

## Family record

```yaml
extensions:
  lucidresume.packages:
    version: "0.1"
    families:
      - id: entity-project-lucidrag
        name: LucidRAG package family
        uri: https://github.com/example/lucidrag
        technologies: [dotnet, retrieval, embeddings]
        observations:
          provider: nuget
          publisher: example
          observed_at: 2026-09-27T12:00:00Z
          package_count: "43"
          total_downloads: "168608"
          package_ids: Example.LucidRAG.Core,Example.LucidRAG.Storage
          source_repository: https://github.com/example/lucidrag
```

Package IDs SHOULD be grouped by a canonical source product where registry
metadata supports that relationship. An implementation MAY use a stable
package-prefix rule when individual packages omit their source repository, but
it MUST retain the individual package IDs so the grouping can be inspected.

`total_downloads` is a time-dependent registry observation. It MUST carry an
`observed_at` value and MUST NOT be converted into a quality or competence
claim. Package tags and descriptions are publisher-authored metadata. They MAY
support retrieval candidates but require normal JobML review before becoming a
personal evidential claim.

The human résumé remains a projection. Package families SHOULD stay in the full
career record unless a target role makes the family directly relevant.
