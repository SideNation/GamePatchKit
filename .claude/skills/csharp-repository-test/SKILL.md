---
name: csharp-repository-test
description: Generates C# data access layer (Repository) test code using xUnit + EF Core InMemory. Covers CRUD and query tests for Repository and DbContext classes. Triggers on "repository test", "DB test", "DbContext test", "EF Core test", "CRUD test", or when the user provides a Repository class and requests tests. For service unit tests use csharp-unit-test; for Controller tests use csharp-api-test.
---

# C# Repository Test Generator (xUnit + EF Core InMemory)

Use NSubstitute only when the Repository has additional dependencies beyond DbContext.

## Test Class Structure

```csharp
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ProjectName.Tests.Data.Repositories;

public class OrderRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly OrderRepository _sut;

    public OrderRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _sut = new OrderRepository(_context);
    }

    public void Dispose() => _context.Dispose();
}
```

- Use `Guid.NewGuid().ToString()` as DB name for test isolation
- Implement `IDisposable` to clean up DbContext

## Seed Data

```csharp
_context.Orders.AddRange(
    new Order { Id = 1, CustomerId = 100, Status = OrderStatus.Pending },
    new Order { Id = 2, CustomerId = 200, Status = OrderStatus.Completed }
);
await _context.SaveChangesAsync();
```

## Write Verification (read-write separation)

```csharp
var dbName = Guid.NewGuid().ToString();
// Save with a write Context, then verify with a separate read Context sharing the same dbName
```

## Required Test Cases

- **Create**: save and retrieve, verify ID generation
- **Read**: existing ID → entity returned; missing ID → null
- **Update**: modify field, verify persisted change
- **Delete**: remove and verify null on re-read; missing ID → exception
- **Queries**: filter result count, sort order, pagination, Include relationship loading
- **Edge**: empty table → empty list, Count 0

## Pattern / Naming

| Pattern       | Naming                                 |
| ------------- | -------------------------------------- |
| AAA (default) | `MethodName_Condition_ExpectedResult`  |
| GWT           | `Should_ExpectedResult_When_Condition` |

## InMemory Limitations

Not supported: foreign key enforcement, transaction rollback, raw SQL, DB-specific functions (`EF.Functions.Like`), concurrency tokens.
When needed, suggest SQLite InMemory alternative:

```csharp
var conn = new SqliteConnection("DataSource=:memory:");
conn.Open();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
```

## Required Packages

xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Microsoft.EntityFrameworkCore.InMemory
Additional dependencies: add NSubstitute
