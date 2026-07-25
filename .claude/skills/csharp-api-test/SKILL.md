---
name: csharp-api-test
description: Generates C# API/Controller test code using xUnit + NSubstitute. Covers ASP.NET Core Controller unit tests and WebApplicationFactory integration tests. Triggers on "API test", "controller test", "endpoint test", "integration test", or when the user provides a Controller class and requests tests. For service unit tests use csharp-unit-test; for Repository tests use csharp-repository-test.
---

# C# API / Controller Test Generator (xUnit + NSubstitute)

## Approach

Default is **unit test**. Use integration test when "integration test", "WebApplicationFactory", or "middleware" is requested.

## Unit Test Structure

```csharp
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace ProjectName.Tests.Api.Controllers;

public class OrdersControllerTests
{
    private readonly IOrderService _orderService;
    private readonly OrdersController _sut;

    public OrdersControllerTests()
    {
        _orderService = Substitute.For<IOrderService>();
        _sut = new OrdersController(_orderService);
    }
}
```

## ActionResult Assertion Reference

| HTTP | Type | Assertion |
|------|------|-----------|
| 200 | `OkObjectResult` | `Assert.IsType<OkObjectResult>(result.Result)` |
| 201 | `CreatedAtActionResult` | `Assert.IsType<CreatedAtActionResult>(result.Result)` |
| 204 | `NoContentResult` | `Assert.IsType<NoContentResult>(result)` |
| 400 | `BadRequestObjectResult` | `Assert.IsType<BadRequestObjectResult>(result.Result)` |
| 404 | `NotFoundResult` | `Assert.IsType<NotFoundResult>(result.Result)` |

Extract value: `var value = Assert.IsType<OrderDto>(okResult.Value);`

## ModelState Validation

```csharp
_sut.ModelState.AddModelError("Field", "Required");
var result = await _sut.Create(new Request());
Assert.IsType<BadRequestObjectResult>(result.Result);
```

## Authentication Context Setup

```csharp
var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
_sut.ControllerContext = new ControllerContext
{
    HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
    }
};
```

## Integration Test Structure (on request)

```csharp
public class OrdersIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly IOrderRepository _repo;

    public OrdersIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _repo = Substitute.For<IOrderRepository>();
        _client = factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddScoped(_ => _repo))
        ).CreateClient();
    }
}
```

Verify status: `Assert.Equal(HttpStatusCode.OK, response.StatusCode);`
Read body: `await response.Content.ReadFromJsonAsync<OrderDto>();`

## Pattern / Naming

| Pattern | Naming |
|---------|--------|
| AAA (default) | `ActionName_Condition_ExpectedStatus` |
| GWT | `Should_ReturnStatus_When_Condition` |

## Required Test Cases

For each action method: success response, not found (404), bad request (400), service exception handling.

## Required Packages

xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, NSubstitute.Analyzers.CSharp
Integration tests: add Microsoft.AspNetCore.Mvc.Testing
