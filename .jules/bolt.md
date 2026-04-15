## 2026-04-15 - Bulk updates using ExecuteUpdateAsync
**Learning:** Found an anti-pattern in `DuplicateDetectionService.cs` where bulk updates were performed by fetching all entities into memory (`.Where().ToListAsync()`), iterating over them to modify properties, and relying on EF Core change tracking to issue individual `UPDATE` statements on `SaveChangesAsync()`.
**Action:** Replaced these loops with EF Core 7+ `ExecuteUpdateAsync` to issue a single `UPDATE` statement directly to the database. This avoids memory allocation, change tracking overhead, and multiple database roundtrips.
