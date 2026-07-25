---
name: csharp-coding-standards
description: Applies team C# coding conventions when writing code. Covers naming rules, code element ordering, and code quality rules. Triggers when writing, generating, modifying, refactoring, or reviewing C# code. Also applies to test code alongside test skills. Use for any task involving "unity", ".cs files", "C# class", "C# code", or "code review".
---

# C# Coding Standards

## Naming Rules

### General

| Target                                          | Convention                 | Example                               |
| ----------------------------------------------- | -------------------------- | ------------------------------------- |
| Class, struct, method, constant, enum, delegate | PascalCase                 | `OrderService`, `CalculateTotal`      |
| Public field, property, event                   | PascalCase                 | `TotalAmount`, `OrderCreated`         |
| Public const field                              | UPPER_CASE                 | `MAX_RETRY_COUNT`, `DEFAULT_TIMEOUT`  |
| Parameter, local variable                       | camelCase                  | `orderId`, `customerName`             |
| Private member field                            | \_camelCase                | `_orderRepository`, `_logger`         |
| Interface                                       | IPascalCase                | `IOrderService`, `IUserRepository`    |
| File name                                       | Match class/interface name | `OrderService.cs`, `IOrderService.cs` |
| Test file name                                  | Test + class name          | `TestOrderService.cs`                 |

### Field or Variable Names

Field or variable names should be descriptive and avoid abbreviations unless widely understood (e.g., `id`, `url`).

| Rule | Avoid | Prefer | Reason |
|------|-------|--------|--------|
| Avoid single letters and abbreviations | `int d`<br>`int hp`<br>`string tName`<br>`int mvmtSpeed` | `int elapsedTimeInDays`<br>`int healthPoints`<br>`string teamName`<br>`int movementSpeed` | Specify units explicitly and use full words instead of abbreviations |
| Use searchable and pronounceable names | `int getMovementSpeed` | `int movementSpeed` | Variable names should clearly indicate their purpose |
| Boolean variables | `bool dead`<br>`bool isPlayerDead` | `bool isDead`<br>`bool isPlayerDead` | Use questions that can be answered with true/false (is, has, can, etc.) |
| Method naming | For non-boolean methods, use verbs in method names | `GetMovementSpeed()`<br>`CalculateTotal()` | Clearly express the action being performed |

## Code Element Ordering

**Place `using` directives outside the namespace.**

Within a file: Delegate → Enum → Interface → Struct → Class

Within a class/struct/interface:
Interfaces → Structs → Classes → Enums → Delegates → Events → static, const and readonly fields → Fields → Properties → Constructors → Finalizers → Methods → Indexers

Adjacent members of the same kind ordered by access level:
public → internal → protected internal → protected → private protected → private

## Code style rules

- Column limit 150, 4-space indent, `end_of_line = lf`
- Opening brace (`{`) always goes on a new line (methods, properties, control blocks, types)
- `else` / `catch` / `finally` go on a new line
- Members of object initializers and anonymous types each go on their own line
- LINQ query clauses stay on a single line
- Leave one blank line after an `if` block
- Indent `switch` `case` labels and their contents; keep ordinary labels flush-left
- Do not put multiple statements on one line; single-line blocks (e.g. auto properties) may stay as-is

## Prohibited

- **No `#region`**
- **No magic numbers/strings** — use constants or configuration values
- **No string `+` concatenation in loops** — use `StringBuilder`

## Examples

See [`examples/CodingStyleExample.cs`](examples/CodingStyleExample.cs) for a single consolidated example covering all rules.
