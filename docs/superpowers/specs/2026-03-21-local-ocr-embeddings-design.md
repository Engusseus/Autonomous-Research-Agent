# Local OCR And Embeddings Design

## Goal

Add a local OCR path and a local embedding path so imported or attached papers can move through this sequence:

1. download document
2. extract text from PDF natively when possible
3. fall back to local OCR when native extraction is weak or empty
4. generate OpenRouter summaries from extracted text
5. generate and persist local embeddings in PostgreSQL pgvector
6. make semantic and hybrid search use the stored vectors

OpenRouter remains the only remote dependency for summarization and analysis.

## Decisions

### OCR

- Keep the existing document-processing job flow.
- Add an injected OCR abstraction inside `PaperDocumentProcessingService`.
- Native extraction remains first.
- OCR runs only when extraction is empty or clearly too weak, or when `RequiresOcr` is set.
- OCR will use local command-line tooling so the app can stay offline for OCR.

### Embeddings

- Replace the placeholder embedding implementation with a local provider-backed implementation.
- Persist embeddings for:
  - paper abstracts
  - summaries
- Use paper-level search results even when the matched vector originated from a summary.
- Keep the schema simple for this pass instead of introducing document-chunk embeddings immediately.

### Search

- Semantic search should stop depending on the placeholder path.
- Query embeddings are generated locally.
- Stored embeddings in `paper_embeddings` become the source of semantic ranking.
- Hybrid search continues to blend keyword and semantic scores, but now semantic scores come from real vectors.

## Scope

### Included

- local OCR service/adapter
- local embedding service/adapter
- config for both
- summary embedding persistence
- paper abstract embedding persistence
- semantic and hybrid search over real vectors
- tests for OCR fallback, embedding persistence, and search behavior

### Deferred

- document chunk embeddings
- OCR confidence metadata in API contracts
- full offline summarization
- vector backfill UX in frontend

## Implementation Shape

### Document Processing

`PaperDocumentProcessingService` will:

- clear stale extraction failure/success fields before reprocessing
- attempt native extraction first
- call OCR when native extraction is insufficient
- save the final extracted text

### Embedding Indexing

Add a dedicated infrastructure service responsible for:

- generating vectors through the local embedding provider
- upserting paper abstract embeddings
- upserting summary embeddings

This service will be called from:

- paper import/create/update flows for abstract embeddings
- summary create/update flows for summary embeddings

### Search Retrieval

`SearchService` will:

- generate a query embedding locally
- load stored paper and summary vectors
- score by cosine similarity in application code for this pass
- aggregate multiple vector hits to a paper-level result

This keeps the first pass compatible with current tests and schema while still producing real semantic behavior.

## Risks

- Local OCR binaries may be missing on some machines; failures should be explicit.
- Snowflake Arctic model downloads are large; configuration must allow an already-downloaded local cache path.
- The current `vector(1536)` column must match the chosen local model dimensions.
- Existing records will not be fully searchable until they are reindexed or touched by new workflows.
