# Local OCR And Embeddings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local OCR and local embeddings so paper documents can be extracted, summarized through OpenRouter, embedded locally, and searched semantically from PostgreSQL.

**Architecture:** Keep the current .NET API and background worker as the orchestrator. Add a local OCR abstraction in document processing and a local embedding abstraction for indexing and search. Persist real vectors in the existing `paper_embeddings` table and have semantic and hybrid search use those stored vectors.

**Tech Stack:** ASP.NET Core, EF Core, PostgreSQL with pgvector, local OCR CLI tooling, local Python embedding runtime, xUnit

---

### Task 1: Add OCR Abstractions And Tests

**Files:**
- Create: `tests/Infrastructure.Tests/PaperDocumentProcessingServiceTests.cs`
- Create: `src/Infrastructure/Services/IDocumentTextExtractor.cs`
- Create: `src/Infrastructure/Services/LocalDocumentTextExtractor.cs`
- Modify: `src/Infrastructure/Services/PaperDocumentProcessingService.cs`
- Modify: `src/Infrastructure/Services/DocumentProcessingOptions.cs`
- Modify: `src/Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- Modify: `src/Api/appsettings.json`
- Modify: `src/Api/appsettings.Development.json`

- [ ] Write failing tests for native extraction, OCR fallback, and stale state clearing.
- [ ] Run targeted tests to verify the new tests fail for the expected reason.
- [ ] Implement the extractor abstraction and wire OCR fallback into `PaperDocumentProcessingService`.
- [ ] Run targeted tests until green.

### Task 2: Add Local Embedding Provider And Indexing Tests

**Files:**
- Create: `tests/Infrastructure.Tests/EmbeddingIndexingServiceTests.cs`
- Create: `src/Infrastructure/Services/LocalEmbeddingOptions.cs`
- Create: `src/Infrastructure/Services/ILocalEmbeddingClient.cs`
- Create: `src/Infrastructure/Services/LocalEmbeddingHttpClient.cs`
- Create: `src/Infrastructure/Services/EmbeddingIndexingService.cs`
- Modify: `src/Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- Modify: `src/Api/appsettings.json`
- Modify: `src/Api/appsettings.Development.json`
- Modify: `src/Infrastructure/Services/PlaceholderEmbeddingService.cs`

- [ ] Write failing tests for paper abstract upsert and summary embedding upsert.
- [ ] Run targeted tests to verify failure first.
- [ ] Implement the local embedding client and indexing service.
- [ ] Run targeted tests until green.

### Task 3: Hook Indexing Into Paper And Summary Workflows

**Files:**
- Modify: `src/Infrastructure/Services/PaperService.cs`
- Modify: `src/Infrastructure/Services/SummaryService.cs`

- [ ] Write failing tests showing paper import/create/update and summary create/update trigger embedding persistence.
- [ ] Run targeted tests to verify failure first.
- [ ] Implement minimal integration with the indexing service.
- [ ] Run targeted tests until green.

### Task 4: Make Semantic Search Use Real Stored Embeddings

**Files:**
- Create: `tests/Infrastructure.Tests/SearchServiceTests.cs`
- Modify: `src/Infrastructure/Services/SearchService.cs`
- Modify: `src/Domain/Enums/EmbeddingType.cs`

- [ ] Write failing tests for semantic ranking from stored vectors and hybrid blending with real semantic scores.
- [ ] Run targeted tests to verify failure first.
- [ ] Implement semantic retrieval over stored vectors and paper-level aggregation.
- [ ] Run targeted tests until green.

### Task 5: Add Local Runtime Support And End-To-End Verification

**Files:**
- Create: `scripts/local_embedding_service.py`
- Create: `scripts/requirements-local-ml.txt`
- Modify: `README.md`

- [ ] Add a minimal local Python embedding service for Snowflake Arctic embeddings.
- [ ] Add setup documentation for local OCR and embedding runtime.
- [ ] Run backend tests and one end-to-end smoke flow if environment dependencies are available.
- [ ] Document any remaining environment gaps clearly.
