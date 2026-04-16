using Microsoft.EntityFrameworkCore;
using AutonomousResearchAgent.Infrastructure.Persistence;

namespace Infrastructure.Tests;

public class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
{
    private readonly ApplicationDbContext _dbContext;

    public TestDbContextFactory(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ApplicationDbContext CreateDbContext()
    {
        return _dbContext;
    }
}
