---
name: csharp-unit-test
description: Generates C# unit test code using xUnit + NSubstitute. Targets business logic classes such as Service, Handler, Manager, Helper, and Validator. Triggers on "unit test", "service test", "mock test", or when the user provides a business logic class and requests tests. For Controller tests use csharp-api-test; for Repository tests use csharp-repository-test.
---

# C# Unit Test Generator (xUnit + NSubstitute)

## Test Class Structure

```csharp
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace ProjectName.Tests.Unit.Services;

public class OrderServiceTests
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<OrderService> _logger;
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _orderRepository = Substitute.For<IOrderRepository>();
        _logger = Substitute.For<ILogger<OrderService>>();
        _sut = new OrderService(_orderRepository, _logger);
    }
}
```

- Use `_sut` for the system under test
- Declare all dependencies as fields; initialize with `Substitute.For<T>()` in the constructor
- No shared state between tests

## Pattern Selection

Default is AAA. Use GWT when "BDD" or "Given-When-Then" is requested.

| Pattern | Naming | Comments |
|---------|--------|----------|
| AAA | `MethodName_Condition_ExpectedResult` | Arrange / Act / Assert |
| GWT | `Should_ExpectedResult_When_Condition` | Given / When / Then |

## NSubstitute Patterns

```csharp
// Return values
service.GetAsync(Arg.Any<int>()).Returns(expected);
service.GetAsync(Arg.Is<int>(x => x > 0)).Returns(expected);
service.GetAsync(Arg.Any<int>()).Returns((Order?)null);

// Exceptions
service.GetAsync(Arg.Any<int>()).ThrowsAsync(new NotFoundException());

// Verification
await service.Received(1).SaveAsync(Arg.Any<Order>());
await service.DidNotReceive().DeleteAsync(Arg.Any<int>());

// Argument capture
Order? captured = null;
await service.SaveAsync(Arg.Do<Order>(o => captured = o));
```

## Required Test Cases

For each public method, include at minimum:

1. **Happy path** — valid input → expected result, verify Received calls
2. **Exception** — dependency throws → `Assert.ThrowsAsync<T>`
3. **Edge cases** — null, empty collection, boundary values, 0/negative ID
4. **Parameterized** — use `[Theory]` + `[InlineData]` for multiple inputs

## Checklist

- Async methods use `async Task` with `await`
- Assert behavior (results and side effects), not implementation details
- Prefer `Arg.Is` over `Arg.Any` where a specific value is known
- Use real instances for plain data objects instead of `Substitute.For`

## Required Packages

xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, NSubstitute.Analyzers.CSharp
