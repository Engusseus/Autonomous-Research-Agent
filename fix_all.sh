#!/bin/bash

# Fix 1: PostgresPasswordValidator
sed -i 's/var connectionString = app.Configuration.GetConnectionString("Postgres") ?? string.Empty;/var configuration = app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();\n            var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;/g' src/Api/Startup/PostgresPasswordValidator.cs

# Fix 2: Unreachable code in HypothesesController
sed -i '72d' src/Api/Controllers/HypothesesController.cs

# Fix 3: ClaimsPrincipalExtensions GetUserId
sed -i 's/public static int? GetUserId/public static Guid? GetUserId/g' src/Api/Extensions/ClaimsPrincipalExtensions.cs
sed -i 's/int.TryParse/Guid.TryParse/g' src/Api/Extensions/ClaimsPrincipalExtensions.cs

# Update all controllers to use Guid? for GetUserId
find src/Api/Controllers -name "*.cs" -exec sed -i 's/private int? GetUserId/private Guid? GetUserId/g' {} +

# Fix 4: WebhooksController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/WebhooksController.cs
sed -i 's/(userId, /(int.Parse(userId.Value.ToString()), /g' src/Api/Controllers/WebhooksController.cs
sed -i 's/(id, userId, /(id, int.Parse(userId.Value.ToString()), /g' src/Api/Controllers/WebhooksController.cs
sed -i 's/(userId, request.Url/(int.Parse(userId.Value.ToString()), request.Url/g' src/Api/Controllers/WebhooksController.cs

# Fix 5: AnnotationsController (expects Guid userId)
sed -i 's/using AutonomousResearchAgent.Application.Annotations;/using AutonomousResearchAgent.Application.Annotations;\nusing AutonomousResearchAgent.Application.Common;/g' src/Api/Controllers/AnnotationsController.cs
sed -i 's/userId.Value,/userId.Value,/g' src/Api/Controllers/AnnotationsController.cs

# Fix 6: CollectionsController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/CollectionsController.cs
sed -i 's/ListAsync(userId,/ListAsync(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/CollectionsController.cs
sed -i 's/GetByIdAsync(id, userId,/GetByIdAsync(id, int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/CollectionsController.cs
sed -i 's/CreateCollectionCommand(userId.Value/CreateCollectionCommand(int.Parse(userId.Value.ToString())/g' src/Api/Controllers/CollectionsController.cs
sed -i 's/, userId, /, int.Parse(userId.Value.ToString()), /g' src/Api/Controllers/CollectionsController.cs

# Fix 7: LiteratureReviewsController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/LiteratureReviewsController.cs
sed -i 's/ListAsync(userId,/ListAsync(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/LiteratureReviewsController.cs
sed -i 's/GetByIdAsync(id, userId,/GetByIdAsync(id, int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/LiteratureReviewsController.cs
sed -i 's/CreateAsync(command, userId,/CreateAsync(command, int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/LiteratureReviewsController.cs
sed -i 's/DeleteAsync(id, userId,/DeleteAsync(id, int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/LiteratureReviewsController.cs

# Fix 8: NotificationsController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/NotificationsController.cs
sed -i 's/ListAsync(request.ToApplicationModel(userId)/ListAsync(request.ToApplicationModel(int.Parse(userId.Value.ToString())))/g' src/Api/Controllers/NotificationsController.cs
sed -i 's/GetUnreadCountAsync(userId,/GetUnreadCountAsync(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/NotificationsController.cs
sed -i 's/MarkAllAsReadAsync(userId,/MarkAllAsReadAsync(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/NotificationsController.cs

# Fix 9: ReadingSessionsController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/ReadingSessionsController.cs
sed -i 's/ReadingSessionQuery(userId,/ReadingSessionQuery(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/ReadingSessionsController.cs
sed -i 's/result.UserId != userId/result.UserId != int.Parse(userId.Value.ToString())/g' src/Api/Controllers/ReadingSessionsController.cs
sed -i 's/existing.UserId != userId/existing.UserId != int.Parse(userId.Value.ToString())/g' src/Api/Controllers/ReadingSessionsController.cs
sed -i 's/CreateReadingSessionCommand(userId,/CreateReadingSessionCommand(int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/ReadingSessionsController.cs

# Fix 10: SavedSearchesController (expects int userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/SavedSearchesController.cs
sed -i 's/request.ToApplicationModel(userId)/request.ToApplicationModel(int.Parse(userId.Value.ToString()))/g' src/Api/Controllers/SavedSearchesController.cs

# Fix 11: SummariesController (expects Guid userId)
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/SummariesController.cs

# Fix 12: HypothesesController
sed -i 's/var userId = GetUserId();/var userId = GetUserId();\n        if (!userId.HasValue) return Unauthorized();/g' src/Api/Controllers/HypothesesController.cs
sed -i 's/ListAsync(userId, /ListAsync(int.Parse(userId.Value.ToString()), /g' src/Api/Controllers/HypothesesController.cs
sed -i 's/CreateAsync(command, userId,/CreateAsync(command, int.Parse(userId.Value.ToString()),/g' src/Api/Controllers/HypothesesController.cs
sed -i 's/existing.UserId != userId.Value/existing.UserId != int.Parse(userId.Value.ToString())/g' src/Api/Controllers/HypothesesController.cs

# Fix 13: ContractMappingExtensions
sed -i 's/model.ReviewedAt,/model.ReviewedAt?.DateTime,/g' src/Api/Extensions/ContractMappingExtensions.cs
