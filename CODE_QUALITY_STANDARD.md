# Code Quality Standard

## Document Information
- **Version**: 1.5
- **Last Updated**: 2026-10-29
- **Scope**: .NET service codebases and the Foundation libraries (the code examples below use an illustrative order-management service)
- **Purpose**: Define and enforce coding standards for consistency, maintainability, and quality

---

## Table of Contents
1. [Type Declarations](#1-type-declarations)
2. [Formatting and Style](#2-formatting-and-style)
3. [Naming Conventions](#3-naming-conventions)
4. [Null Reference Handling](#4-null-reference-handling)
5. [Async/Await Patterns](#5-asyncawait-patterns)
6. [Testing Standards](#6-testing-standards)
7. [AutoFixture Patterns](#7-autofixture-patterns)
8. [Architecture and Design](#8-architecture-and-design)
9. [Entity Framework and Data Access](#9-entity-framework-and-data-access)
10. [Documentation and Comments](#10-documentation-and-comments)
11. [Validation and Error Handling](#11-validation-and-error-handling)
12. [File Organization](#12-file-organization)
13. [LINQ and Query Patterns](#13-linq-and-query-patterns)
14. [Dependency Injection](#14-dependency-injection)

---

## 1. Type Declarations

### 1.1 Explicit Type Usage
**RULE**: Always use explicit types. Never use the `var` keyword.

**Rationale**: Explicit types improve code readability, make the code self-documenting, and prevent ambiguity about variable types.

✅ **CORRECT:**
```csharp
Fixture fixture = new Fixture();
Mock<IAddressRepository> repositoryMock = new Mock<IAddressRepository>();
List<Person> Persons = new List<Person>();
Person Person = await repository.GetPersonByID(id);
```

❌ **INCORRECT:**
```csharp
var fixture = new Fixture();
var repositoryMock = new Mock<IAddressRepository>();
var Persons = new List<Person>();
var Person = await repository.GetPersonByID(id);
```

### 1.2 Nullable Reference Types
**RULE**: Enable nullable reference types and use `?` to explicitly mark nullable types.

✅ **CORRECT:**
```csharp
public string? HomePhone { get; set; }
public Person? GetPersonById(int id);
if (entity == null) return null;
```

### 1.3 Required Properties
**RULE**: Use the `required` modifier for non-nullable properties that must be initialized.

✅ **CORRECT:**
```csharp
public required string FirstName { get; set; }
public required Person Person { get; set; }
```

---

## 2. Formatting and Style

### 2.1 Indentation
**RULE**: Use **4 spaces** for indentation. Never use tabs.

**Configuration**: Set your IDE to convert tabs to spaces.

### 2.2 Line Length
**RULE**: Keep lines under **120 characters** where practical. Break long lines for readability.

✅ **CORRECT:**
```csharp
Person Person = _fixture.Build<Person>()
    .With(i => i.FirstName, "John")
    .With(i => i.LastName, "Doe")
    .Without(i => i.Addresses)
    .Without(i => i.Relationships)
    .Create();
```

### 2.3 Brace Placement
**RULE**: Opening braces on the same line for control structures, new line for type declarations.

✅ **CORRECT:**
```csharp
// Type declarations - new line
public class Person
{
    // Control structures - same line
    if (condition) {
        // code
    }

    public void Method()
    {
        for (int i = 0; i < 10; i++) {
            // code
        }
    }
}
```

### 2.4 Spacing
**RULE**:
- One blank line between methods
- No blank lines at the start or end of blocks
- Space after keywords (`if`, `for`, `while`)
- Space around operators (`=`, `+`, `-`, `==`)

✅ **CORRECT:**
```csharp
public async Task<Person> GetById(int id)
{
    Person Person = await _repository.GetPersonByID(id);

    if (Person == null) {
        throw new ResourceNotFoundException($"Person with ID {id} not found");
    }

    return Person;
}
```

### 2.5 Using Directives
**RULE**: Place all `using` directives at the top of the file, sorted alphabetically. Use file-scoped namespaces (C# 10+).

✅ **CORRECT:**
```csharp
using Order_Service_DAL.Entities;
using Order_Service_DAL.Repositories;
using AutoFixture;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Diagnostics.CodeAnalysis;

namespace Order_Service_DAL_Tests.Repositories;

[TestClass]
public class OrderRepositoryTests
{
    // class content
}
```

---

## 3. Naming Conventions

### 3.1 General Naming Rules

| Element | Convention | Example |
|---------|-----------|---------|
| Classes | PascalCase | `OrderRepository`, `AddressMapper` |
| Interfaces | PascalCase with `I` prefix | `IOrderRepository`, `IAddressMapper` |
| Methods | PascalCase | `GetPersonByID`, `CreateAddress` |
| Public Properties | PascalCase | `FirstName`, `DateOfBirth` |
| Private Fields | camelCase with `_` prefix | `_fixture`, `_repository`, `_context` |
| Parameters | camelCase | `PersonId`, `homePhone` |
| Local Variables | camelCase | `Person`, `addressList` |
| Constants | PascalCase | `TestHomePhone`, `MaxRetryCount` |
| Async Methods | PascalCase + `Async` suffix | `GetPersonByIDAsync`, `CreateAddressAsync` |

### 3.2 Test Method Naming
**RULE**: Use the pattern `MethodName_ExpectedBehavior_WhenCondition`

✅ **CORRECT:**
```csharp
[TestMethod]
public async Task GetPersonByID_ReturnsPerson_WhenExists()

[TestMethod]
public async Task CreateAddress_ThrowsException_WhenPersonNotFound()

[TestMethod]
public async Task ToModel_ReturnsNull_WhenEntityIsNull()

[TestMethod]
public async Task SoftDeletePerson_SetsIsActiveFalse()
```

### 3.3 Boolean Naming
**RULE**: Use affirmative names with `Is`, `Has`, `Can`, `Should` prefixes.

✅ **CORRECT:**
```csharp
public bool IsActive { get; set; }
public bool IsVeteran { get; set; }
public bool HasInsurance { get; set; }
```

❌ **INCORRECT:**
```csharp
public bool NotActive { get; set; }  // Avoid negative names
public bool Active { get; set; }     // Missing 'Is' prefix
```

### 3.4 Collection Naming
**RULE**: Use plural nouns for collections.

✅ **CORRECT:**
```csharp
public ICollection<Address> Addresses { get; set; }
public List<Person> Persons { get; set; }
```

---

## 4. Null Reference Handling

### 4.1 Null Checks
**RULE**: Always perform explicit null checks on method parameters and return values where appropriate.

✅ **CORRECT:**
```csharp
public async Task<PersonModel> ToModel(Person entity)
{
    // Explicit null check with early return
    if (entity == null) {
        return null;
    }

    return new PersonModel
    {
        Id = entity.Id,
        FirstName = entity.FirstName,
        // ...
    };
}
```

### 4.2 Null-Conditional Operators
**RULE**: Use null-conditional operators (`?.`) when appropriate to avoid verbose null checks.

✅ **CORRECT:**
```csharp
int? memoryLossId = entity.MemoryLossInidcator?.Id;
string upperName = Person?.FirstName?.ToUpper();
```

### 4.3 Null-Coalescing Operators
**RULE**: Use null-coalescing operators (`??`, `??=`) for default values.

✅ **CORRECT:**
```csharp
string displayName = Person.FirstName ?? "Unknown";
_cache ??= new Dictionary<int, Person>();
```

### 4.4 ArgumentNullException
**RULE**: For public APIs and critical methods, throw `ArgumentNullException` for null arguments that should never be null.

✅ **CORRECT:**
```csharp
public void ProcessPerson(Person Person)
{
    ArgumentNullException.ThrowIfNull(Person);
    // process Person
}
```

**Exception**: Mapper methods may return null for null input as per domain requirements.

---

## 5. Async/Await Patterns

### 5.1 Async Method Naming
**RULE**: All async methods must end with the `Async` suffix.

✅ **CORRECT:**
```csharp
public async Task<Person> GetPersonByIDAsync(int id)
public async Task UpdatePersonAsync(Person Person)
public async Task DeleteAddressAsync(int id)
```

❌ **INCORRECT:**
```csharp
public async Task<Person> GetPersonByID(int id)  // Missing Async suffix
```

**Exception**: Methods decorated with `[TestMethod]` do not require the `Async` suffix. Helper and private async methods in a `[TestClass]` file are still required to carry the suffix.

### 5.2 Return Types
**RULE**:
- Use `Task<T>` for async methods that return a value
- Use `Task` for async methods that don't return a value
- Never use `async void` except for event handlers

✅ **CORRECT:**
```csharp
public async Task<Person> GetByIdAsync(int id)
public async Task UpdateAsync(Person Person)
```

### 5.3 Await Usage
**RULE**: Always use `await` for async calls. Don't use `.Result` or `.Wait()`.

✅ **CORRECT:**
```csharp
Person Person = await _repository.GetPersonByID(id);
await _repository.UpdatePerson(Person);
```

❌ **INCORRECT:**
```csharp
Person Person = _repository.GetPersonByID(id).Result;  // Blocking!
_repository.UpdatePerson(Person).Wait();                    // Blocking!
```

### 5.4 ConfigureAwait
**RULE**: In library code, use `ConfigureAwait(false)` to avoid deadlocks. In ASP.NET Core, this is optional as it uses a different synchronization context.

```csharp
// Optional in ASP.NET Core 3.0+, but acceptable
Person Person = await _repository.GetPersonByID(id).ConfigureAwait(false);
```

---

## 6. Testing Standards

### 6.1 Test Structure
**RULE**: Use the **Arrange-Act-Assert** (AAA) pattern for all tests.

✅ **CORRECT:**
```csharp
[TestMethod]
public async Task GetPersonByID_ReturnsPerson_WhenExists()
{
    // Arrange
    Person Person = _fixture.Build<Person>()
        .Without(i => i.Addresses)
        .Without(i => i.Relationships)
        .Create();
    _context.Persons.Add(Person);
    await _context.SaveChangesAsync();

    // Act
    Person result = await _repository.GetPersonByID(Person.Id);

    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(Person.Id, result.Id);
    Assert.AreEqual(Person.FirstName, result.FirstName);
}
```

### 6.2 Test Attributes
**RULE**: Use appropriate test attributes:
- `[TestClass]` on test classes
- `[TestMethod]` on test methods
- `[TestInitialize]` for setup
- `[TestCleanup]` for teardown
- `[ExcludeFromCodeCoverage]` or `[ExcludeFromCodeCoverageAttribute]` on **all test classes**

**Rationale**: Test classes should be excluded from code coverage metrics as they are not production code and covering tests with tests is redundant.

✅ **CORRECT:**
```csharp
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
[TestClass]
public class OrderRepositoryTests
{
    [TestInitialize]
    public void Setup() { }

    [TestMethod]
    public async Task GetPersonByID_ReturnsPerson_WhenExists() { }

    [TestCleanup]
    public void Cleanup() { }
}
```

❌ **INCORRECT:**
```csharp
// Missing [ExcludeFromCodeCoverage]
[TestClass]
public class OrderRepositoryTests
{
    // ...
}
```

### 6.3 One Assertion Concept Per Test
**RULE**: Each test should verify one logical concept, though multiple assertions on the same object are acceptable.

✅ **CORRECT:**
```csharp
[TestMethod]
public async Task ToModel_MapsAllProperties_WhenEntityIsValid()
{
    // Arrange & Act
    PersonModel result = await _mapper.ToModel(entity);

    // Assert - Multiple assertions on same concept (mapping correctness)
    Assert.IsNotNull(result);
    Assert.AreEqual(entity.Id, result.Id);
    Assert.AreEqual(entity.FirstName, result.FirstName);
    Assert.AreEqual(entity.LastName, result.LastName);
}
```

### 6.4 Test Independence
**RULE**: Tests must be independent and able to run in any order.
- Use unique database instances per test (via `Guid.NewGuid()`)
- Don't share state between tests
- Clean up resources in `[TestCleanup]`

✅ **CORRECT:**
```csharp
[TestInitialize]
public void Setup()
{
    // Each test gets its own database instance
    _dbOptions = new DbContextOptionsBuilder<PersonContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
}
```

### 6.5 Mock Naming
**RULE**: Suffix mock variables with `Mock`.

✅ **CORRECT:**
```csharp
Mock<IOrderRepository> _OrderRepositoryMock;
Mock<IAddressMapper> _addressMapperMock;
```

---

## 7. AutoFixture Patterns

### 7.1 Circular Reference Handling
**RULE**: Always configure AutoFixture to handle circular references in test setup.

✅ **CORRECT:**
```csharp
[TestInitialize]
public void Setup()
{
    _fixture = new Fixture();

    // Configure AutoFixture to handle circular references gracefully
    _fixture.Behaviors
        .OfType<ThrowingRecursionBehavior>()
        .ToList()
        .ForEach(b => _fixture.Behaviors.Remove(b));
    _fixture.Behaviors.Add(new OmitOnRecursionBehavior());
}
```

### 7.2 Entity Customization
**RULE**: Customize entities with validation requirements in `[TestInitialize]`.

✅ **CORRECT:**
```csharp
// Customize Person entity to ensure valid test data
_fixture.Customize<Person>(c => c
    .With(i => i.HomePhone, () => $"555-1{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")
    .With(i => i.WorkPhone, () => $"555-2{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")
    .With(i => i.MobilePhone, () => $"555-3{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")
    .Without(i => i.Addresses)
    .Without(i => i.Relationships)
);
```

### 7.3 Distinct Value Ranges
**RULE**: When multiple related fields must have different values, use distinct ranges.

**Phone Number Pattern**:
- HomePhone: `555-1XX-XXXX` (range 555-110 to 555-199)
- WorkPhone: `555-2XX-XXXX` (range 555-210 to 555-299)
- MobilePhone: `555-3XX-XXXX` (range 555-310 to 555-399)

This ensures the three phone types are always different from each other.

### 7.4 Navigation Property Omission
**RULE**: Omit navigation properties in AutoFixture customizations to prevent circular references and allow explicit test setup.

✅ **CORRECT:**
```csharp
_fixture.Customize<Person>(c => c
    .Without(i => i.Addresses)      // Omit to avoid circular references
    .Without(i => i.Relationships)  // Omit to avoid circular references
);

_fixture.Customize<Address>(c => c
    .Without(a => a.Person)  // Omit to avoid circular references
);
```

### 7.5 Explicit Test Data Setup
**RULE**: For specific test scenarios, explicitly build test data rather than relying on default AutoFixture behavior.

✅ **CORRECT:**
```csharp
// Explicit setup for specific test scenario
Person Person = _fixture.Build<Person>()
    .With(i => i.FirstName, "John")
    .With(i => i.LastName, "Doe")
    .With(i => i.IsActive, true)
    .Create();
```

---

## 8. Architecture and Design

### 8.1 Layered Architecture
**RULE**: Maintain strict separation of concerns across layers:
- **API Layer**: Controllers, HTTP concerns
- **Business Logic Layer**: Business rules, orchestration
- **Data Access Layer (DAL)**: Repositories, Entity Framework
- **DTO Layer**: Data Transfer Objects, Mappers

**Dependencies flow**: API → BusinessLogic → DAL

### 8.2 Repository Pattern
**RULE**: All data access must go through repository interfaces.

✅ **CORRECT:**
```csharp
public interface IOrderRepository
{
    Task<Person> GetPersonByID(int id);
    Task<Person> CreatePerson(Person Person);
    Task UpdatePerson(Person Person);
    Task DeletePerson(int id);
}
```

### 8.3 Mapper Pattern
**RULE**: Use dedicated mapper classes to convert between entities and DTOs.

✅ **CORRECT:**
```csharp
public interface IPersonMapper
{
    Task<PersonModel> ToModel(Person entity);
    Task<PersonBaseModel> ToBaseModel(Person entity);
    Task<Person> ToEntity(PersonBaseModel model);
}
```

### 8.4 POCO Classes and Code Coverage
**RULE**: Apply `[ExcludeFromCodeCoverage]` attribute to all POCO (Plain Old CLR Object) classes including:
- Entity classes (data models)
- DTO classes (Data Transfer Objects)
- Model classes
- Configuration classes
- Simple data containers with no business logic

**Rationale**: POCO classes typically contain only properties and simple data structures without business logic. Code coverage on these classes provides minimal value and inflates coverage metrics without meaningful testing.

✅ **CORRECT:**
```csharp
using System.Diagnostics.CodeAnalysis;

// Entity/Model class
[ExcludeFromCodeCoverage]
public class Person : BaseEntity
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public ICollection<Address> Addresses { get; set; } = [];
}

// DTO class
[ExcludeFromCodeCoverage]
public class PersonModel
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime CreatedDate { get; set; }
}

// Base Model class
[ExcludeFromCodeCoverage]
public class PersonBaseModel
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Email { get; set; }
}
```

❌ **INCORRECT:**
```csharp
// Missing [ExcludeFromCodeCoverage] on POCO class
public class Person : BaseEntity
{
    public required string FirstName { get; set; }
    // ...
}
```

**Exception**: Do NOT apply `[ExcludeFromCodeCoverage]` to classes with business logic, validation logic, or computed properties that require testing.

### 8.5 Dependency Injection
**RULE**: All dependencies must be injected through constructor injection.

✅ **CORRECT:**
```csharp
public class PersonFunctions : IPersonFunctions
{
    private readonly IOrderRepository _repository;
    private readonly IPersonMapper _mapper;

    public PersonFunctions(
        IOrderRepository repository,
        IPersonMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }
}
```

❌ **INCORRECT:**
```csharp
// Don't use service locator or static dependencies
public class PersonFunctions
{
    private IOrderRepository _repository = ServiceLocator.Get<IOrderRepository>();
}
```

### 8.7 EF Migration Files and Code Coverage
**RULE**: Apply `[ExcludeFromCodeCoverage]` to all EF Core migration files inside a `Migrations/` folder:
- `*_<Name>.cs` — the migration class (Up/Down methods)
- `*_<Name>.Designer.cs` — the auto-generated model snapshot for that migration
- `*ModelSnapshot.cs` — the cumulative context snapshot

**Rationale**: EF Core auto-generates these files. They contain no application logic, cannot be meaningfully unit-tested, and their line counts (often thousands of lines) heavily dilute reported coverage percentages.

The attribute belongs on the migration `.cs` file (the partial class declaration site). The companion `.Designer.cs` shares the same `partial class` — placing the attribute on both parts causes CS0579 (duplicate attribute).

✅ **CORRECT — `20260522102902_AddRelationshipTypes.cs`:**
```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using System.Diagnostics.CodeAnalysis;

namespace MyProject.Migrations
{
    [ExcludeFromCodeCoverage]
    public partial class AddRelationshipTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) { }
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
```

✅ **CORRECT — `PersonContextModelSnapshot.cs`:**
```csharp
using System.Diagnostics.CodeAnalysis;

namespace MyProject.Migrations
{
    [DbContext(typeof(PersonContext))]
    [ExcludeFromCodeCoverage]
    partial class PersonContextModelSnapshot : ModelSnapshot { }
}
```

❌ **INCORRECT — attribute missing from migration .cs:**
```csharp
// Missing [ExcludeFromCodeCoverage] — inflates uncovered line count
public partial class AddRelationshipTypes : Migration { }
```

❌ **INCORRECT — attribute on both .cs and .Designer.cs (CS0579 duplicate):**
```csharp
// In AddRelationshipTypes.cs
[ExcludeFromCodeCoverage]
public partial class AddRelationshipTypes : Migration { }

// Also in AddRelationshipTypes.Designer.cs  ← DO NOT add here
[ExcludeFromCodeCoverage]
partial class AddRelationshipTypes { }
```

**Note**: The post-edit-validate hook (§8.7) enforces this rule automatically whenever a Designer.cs or ModelSnapshot.cs inside a Migrations folder is edited.

**Important**: `[ExcludeFromCodeCoverage]` is **NOT valid on `enum` declarations** (CS0592). Enums have no executable IL, so they do not contribute to coverage percentages regardless — do not attempt to add this attribute to enums.

### 8.8 Minimum Coverage Threshold
**RULE**: Every non-test assembly must maintain **≥ 90% line coverage**. The Stop hook (`build-and-test.ps1`) enforces this automatically — if any production assembly drops below 90% the agent is blocked from finishing its turn.

**What counts toward the threshold:**
- All non-excluded classes, methods, and properties in the assembly
- Generated files (`*.generated.cs`, `obj/` files) are excluded via `coverlet.runsettings`

**Acceptable ways to reach 90%:**
1. Add unit tests for uncovered business-logic paths (preferred)
2. Apply `[ExcludeFromCodeCoverage]` to infrastructure-only code that cannot be meaningfully unit-tested (DI wiring, design-time factories, HTTP context accessors, Gremlin connection factories, etc.)

**NOT acceptable:**
- Lowering the threshold in the hook
- Suppressing coverage collection globally

### 8.6 Interface Segregation
**RULE**: Create focused interfaces with single responsibilities.

✅ **CORRECT:**
```csharp
public interface IPersonFunctions
{
    Task<PersonModel> GetById(int id);
    Task<PersonModel> CreatePerson(PersonBaseModel model);
    Task UpdateById(int id, PersonModel model);
    Task SoftDeleteById(int id);
}

public interface IAddressFunctions
{
    Task<AddressModel> GetAddressById(int id);
    Task<List<AddressModel>> CreateAddresses(List<AddressBaseModel> addresses, Person Person);
}
```

---

## 9. Entity Framework and Data Access

### 9.1 AsNoTracking for Read-Only Queries
**RULE**: Use `AsNoTracking()` for queries that don't update entities.

✅ **CORRECT:**
```csharp
public async Task<List<Person>> Search(string firstName, string lastName)
{
    return await _context.Persons
        .AsNoTracking()  // Read-only query
        .Where(i => i.FirstName == firstName && i.LastName == lastName)
        .ToListAsync();
}
```

### 9.2 Eager Loading with Include
**RULE**: Use `Include()` to eagerly load navigation properties needed by the query.

✅ **CORRECT:**
```csharp
public async Task<List<PersonToPersonRelationship>> GetPersonToPersonRelationshipsByPersonID(int PersonId)
{
    return await context.PersonToPersonRelationships
        .Include(r => r.FromPerson)  // Explicitly load navigation property
        .AsNoTracking()
        .Where(r => r.FromPerson.Id == PersonId)
        .ToListAsync();
}
```

### 9.3 InMemory Database for Testing
**RULE**: Use unique InMemory database instances for each test.

✅ **CORRECT:**
```csharp
_dbOptions = new DbContextOptionsBuilder<PersonContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())  // Unique per test
    .Options;
```

### 9.4 Base Entity Pattern
**RULE**: Inherit common properties from a base entity class.

✅ **CORRECT:**
```csharp
[ExcludeFromCodeCoverage]
public class BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public required bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }
    public int CreatedById { get; set; }
    public int UpdatedById { get; set; }

    // Optimistic-concurrency token (see §15.2). Mapped for relational providers by
    // ApplyFoundationConcurrencyTokens: a rowversion column on SQL Server, the system xmin column on
    // PostgreSQL. An ordinary property under the InMemory test provider.
    public byte[]? RowVersion { get; set; }
}

public class Case : BaseEntity
{
    // Case-specific properties
}
```

### 9.5 Navigation Properties
**RULE**:
- Use `ICollection<T>` for one-to-many relationships
- Initialize collections with collection initializers `= []`
- Use `required` for non-nullable navigation properties

✅ **CORRECT:**
```csharp
public class Person : BaseEntity
{
    public ICollection<Address> Addresses { get; set; } = [];
    public ICollection<PersonToPersonRelationship> Relationships { get; set; } = [];
}

public class Address : BaseEntity
{
    public required Person Person { get; set; }
}
```

---

## 10. Documentation and Comments

### 10.1 Comment Purpose
**RULE**: Comments should explain **WHY**, not **WHAT**. The code itself should be self-explanatory for "what".

✅ **CORRECT:**
```csharp
// Configure AutoFixture to handle circular references gracefully
// By default, AutoFixture throws when it encounters circular references
// We remove the ThrowingRecursionBehavior and add OmitOnRecursionBehavior to break the cycle
_fixture.Behaviors
    .OfType<ThrowingRecursionBehavior>()
    .ToList()
    .ForEach(b => _fixture.Behaviors.Remove(b));
_fixture.Behaviors.Add(new OmitOnRecursionBehavior());
```

❌ **INCORRECT:**
```csharp
// Remove throwing recursion behavior
_fixture.Behaviors
    .OfType<ThrowingRecursionBehavior>()
    .ToList()
    .ForEach(b => _fixture.Behaviors.Remove(b));
// Add omit on recursion behavior
_fixture.Behaviors.Add(new OmitOnRecursionBehavior());
```

### 10.2 Test Setup Documentation
**RULE**: Document test setup methods comprehensively, explaining:
- What each configuration does
- Why it's necessary
- What patterns or ranges are being used
- What problems it solves

✅ **CORRECT:**
```csharp
[TestInitialize]
public void Setup()
{
    // Initialize AutoFixture for generating test data
    _fixture = new Fixture();

    // Customize Person entity to ensure valid test data
    // Phone numbers must match RegularExpression validation patterns in the entity
    // Each phone type uses a distinct range to ensure they are never equal:
    //   - HomePhone:   555-1XX-XXXX (range 555-110 to 555-199)
    //   - WorkPhone:   555-2XX-XXXX (range 555-210 to 555-299)
    //   - MobilePhone: 555-3XX-XXXX (range 555-310 to 555-399)
    _fixture.Customize<Person>(c => c
        .With(i => i.HomePhone, () => $"555-1{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")
        // ...
    );
}
```

### 10.3 Complex Logic Documentation
**RULE**: Document complex business logic, algorithms, or non-obvious code patterns.

✅ **CORRECT:**
```csharp
// Generate a unique phone number in the 555-1XX-XXXX range
// The modulo operations ensure:
//   - Middle digits are between 10-99 (90 possible values)
//   - Last 4 digits are between 1000-9999 (9000 possible values)
// This provides 810,000 unique phone numbers in this range
$"555-1{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}"
```

### 10.4 XML Documentation
**RULE**: Use XML documentation for public APIs, interfaces, and public methods.

✅ **CORRECT:**
```csharp
/// <summary>
/// Retrieves an Person by their unique identifier.
/// </summary>
/// <param name="id">The unique identifier of the Person.</param>
/// <returns>The Person if found; otherwise, null.</returns>
/// <exception cref="ArgumentOutOfRangeException">Thrown when id is less than or equal to 0.</exception>
public async Task<Person?> GetPersonByID(int id)
{
    // implementation
}
```

### 10.5 TODO Comments
**RULE**: Use TODO comments sparingly and always include context and owner.

✅ **CORRECT:**
```csharp
// TODO (YourName - 2026-05-06): Refactor to use async validation
// Current synchronous validation blocks the thread pool
```

---

## 11. Validation and Error Handling

### 11.1 Data Annotations
**RULE**: Use data annotation attributes for validation on entities and DTOs.

✅ **CORRECT:**
```csharp
[StringLength(200)]
public required string FirstName { get; set; }

[RegularExpression(@"^\d{3}-\d{2}-\d{4}$",
    ErrorMessage = "SSN must be in the format ###-##-####")]
public string? SSN { get; set; }

[RegularExpression(@"^(?:\+1\s?)?(?:\([2-9][0-9]{2}\)|[2-9][0-9]{2})[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}$",
    ErrorMessage = "Phone number must be a valid phone number")]
public string? HomePhone { get; set; }
```

### 11.2 Custom Exceptions
**RULE**: Use domain-specific custom exceptions for business logic errors.

✅ **CORRECT:**
```csharp
// Custom exception for resource not found scenarios
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

// Usage
if (Person == null) {
    throw new ResourceNotFoundException($"Person with ID {id} not found");
}
```

### 11.3 Exception Propagation
**RULE**: Let exceptions propagate naturally unless you can handle them meaningfully at the current level.

✅ **CORRECT:**
```csharp
public async Task<Person> GetById(int id)
{
    // Let repository exceptions propagate
    Person Person = await _repository.GetPersonByID(id);

    if (Person == null) {
        throw new ResourceNotFoundException($"Person {id} not found");
    }

    return Person;
}
```

❌ **INCORRECT:**
```csharp
public async Task<Person> GetById(int id)
{
    try {
        Person Person = await _repository.GetPersonByID(id);
        return Person;
    }
    catch (Exception ex) {
        // Don't catch and rethrow without adding value
        throw;
    }
}
```

### 11.4 Controller Exception Handling — centralized via ProblemDetails
**RULE**: Controllers do **not** wrap actions in try/catch. They throw (or let exceptions propagate)
and a single global `IExceptionHandler` (`GlobalExceptionHandler`, wired via `AddProblemDetails()` +
`AddExceptionHandler<GlobalExceptionHandler>()` + `UseExceptionHandler()`) translates exceptions into
RFC 7807 **ProblemDetails** responses. This keeps actions lean and the error contract uniform (see §15.1).

Mapping: `ResourceNotFoundException` → 404; `ResourceExistsException` / `ConcurrencyConflictException`
→ 409; `DuplicateOrderException` → 409 (with the existing order id exposed as a `orderId` ProblemDetails
extension); `ArgumentException` / `InvalidOperationException` → 400; anything else → 500 (internal
detail suppressed for PII/PHI safety and logged). `[ApiController]` model-validation failures are
already `ValidationProblemDetails` (400).

✅ **CORRECT — lean action, throws for not-found, no try/catch:**
```csharp
[HttpGet("{caseId:int}")]
public async Task<IActionResult> GetOrderAsync(int orderId)
{
    OrderModel result = await _orderFunctions.GetByIdAsync(orderId);
    string? etag = ToETag(result.RowVersion);
    if (etag is not null) Response.Headers.ETag = etag;
    return Ok(result);
}
```

❌ **INCORRECT — per-action try/catch returning raw strings (superseded by the global handler):**
```csharp
[HttpGet("{caseId:int}")]
public async Task<IActionResult> GetOrderAsync(int orderId)
{
    try { return Ok(await _orderFunctions.GetByIdAsync(orderId)); }
    catch (ResourceNotFoundException ex) { return NotFound(ex.Message); }   // don't — handler does this
    catch (Exception ex) { return StatusCode(500, "An unexpected error occurred"); }
}
```

**Exception**: explicit, non-exception `BadRequest(...)` returns for input problems (null patch
document, invalid file/content-type, `!ModelState.IsValid`) remain in the action — they are normal
control flow, not error handling.

### 11.5 Exception Logging — injected ILogger, never static Serilog.Log
**RULE**: Where exceptions ARE caught (the `GenericCrud<TEntity>` data-access base and the
`GlobalExceptionHandler`), log via an **injected `ILogger<T>`** — never the static `Serilog.Log`
(the post-edit hook flags it). Expected business outcomes — `ResourceNotFoundException`,
`ResourceExistsException`, `ConcurrencyConflictException`, `DuplicateOrderException` — are mapped to
status codes (404/409) and are **not** logged as errors (they would be noise). Only genuine faults
(`DbUpdateException`, `NullReferenceException`, unhandled `Exception`/500) are logged.

**Where catches live now**: most code throws and stays catch-free. `GenericCrud` catches around its
EF operations (logging `DbUpdateException`/`NullReferenceException`/system errors, then rethrowing —
value-adding, so it satisfies §11.3; `DbUpdateConcurrencyException` is translated to
`ConcurrencyConflictException`). The global handler logs 500s once, centrally. Controllers and
Functions classes do not catch.

✅ **CORRECT — log all non-business-outcome catches:**
```csharp
try {
    await _repository.UpdateOrderAsync(orderEntity);
}
catch (ResourceNotFoundException) {
    // Expected outcome — no logging
    throw;
}
catch (DbUpdateException ex) {
    _logger.LogError(ex, "Database error updating case with ID {Id}", orderEntity.Id);
    throw;
}
catch (Exception ex) {
    _logger.LogError(ex, "Unexpected error updating case with ID {Id}", orderEntity.Id);
    throw;
}
```

❌ **INCORRECT — catch without logging:**
```csharp
catch (Exception ex) {
    // Missing _logger.LogError call — exception disappears silently
    return StatusCode(500, "An unexpected error occurred");
}
```

❌ **INCORRECT — logging a ResourceNotFoundException:**
```csharp
catch (ResourceNotFoundException ex) {
    _logger.LogError(ex, "Case not found");  // Noise — this is an expected outcome
    return NotFound(ex.Message);
}
```

**ILogger injection pattern:**
```csharp
public class OrderFunctions : IOrderFunctions
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<OrderFunctions> _logger;

    public OrderFunctions(
        IOrderRepository repository,
        ILogger<OrderFunctions> logger)
    {
        _repository = repository;
        _logger = logger;
    }
}
```

---

## 12. File Organization

### 12.1 One Class Per File
**RULE**: Each file should contain exactly one class, interface, or enum. File name must match the type name.

✅ **CORRECT:**
```
OrderRepository.cs        → class OrderRepository
IOrderRepository.cs       → interface IOrderRepository
Person.cs                  → class Person
Gender.cs                      → enum Gender
```

### 12.2 Namespace Structure
**RULE**: Namespaces should mirror folder structure.

✅ **CORRECT:**
```
Folder: Order-Service-DAL/Repositories/
File:   OrderRepository.cs
Namespace: Order_Service_DAL.Repositories;
```

### 12.3 Folder Structure by Layer
**RULE**: Organize code by architectural layer:

```
Order-Service/
├── Order-Service/              (API Layer)
│   └── Controllers/
├── Order-Service-BusinessLogic/ (Business Logic Layer)
├── Order-Service-DAL/          (Data Access Layer)
│   ├── Entities/
│   ├── Repositories/
│   └── Common/
├── Order-Service-DTO/          (DTO Layer)
│   ├── Models/
│   └── Mappers/
└── Tests/
    ├── Order-Service-Tests/
    ├── Order-Service-BusinessLogic-Tests/
    ├── Order-Service-DAL-Tests/
    └── Order-Service-DTO-Tests/
```

### 12.4 Test File Naming
**RULE**: Test files should be named `{ClassUnderTest}Tests.cs`

✅ **CORRECT:**
```
OrderRepository.cs       → OrderRepositoryTests.cs
AddressMapper.cs              → AddressMapperTests.cs
PersonFunctions.cs        → PersonFunctionsTests.cs
PersonController.cs       → PersonControllerTests.cs
```

---

## 13. LINQ and Query Patterns

### 13.1 Method Syntax vs Query Syntax
**RULE**: Prefer **method syntax** over query syntax for LINQ.

✅ **CORRECT:**
```csharp
List<Person> activePersons = await _context.Persons
    .Where(i => i.IsActive)
    .OrderBy(i => i.LastName)
    .ThenBy(i => i.FirstName)
    .ToListAsync();
```

❌ **AVOID:**
```csharp
List<Person> activePersons = await (
    from i in _context.Persons
    where i.IsActive
    orderby i.LastName, i.FirstName
    select i
).ToListAsync();
```

### 13.2 Async LINQ Methods
**RULE**: Use async LINQ methods (`ToListAsync()`, `FirstOrDefaultAsync()`, etc.) for database queries.

✅ **CORRECT:**
```csharp
Person Person = await _context.Persons
    .FirstOrDefaultAsync(i => i.Id == id);

List<Address> addresses = await _context.Addresses
    .Where(a => a.Person.Id == PersonId)
    .ToListAsync();
```

### 13.3 Expression Readability
**RULE**: Break complex LINQ expressions across multiple lines for readability.

✅ **CORRECT:**
```csharp
List<Person> results = await _context.Persons
    .AsNoTracking()
    .Where(i => i.FirstName == firstName)
    .Where(i => i.LastName == lastName)
    .Where(i => i.IsActive)
    .Include(i => i.Addresses)
    .OrderBy(i => i.CreatedDate)
    .ToListAsync();
```

---

## 14. Dependency Injection

### 14.1 Constructor Injection
**RULE**: Use constructor injection for all dependencies.

✅ **CORRECT:**
```csharp
public class PersonController : ControllerBase
{
    private readonly IPersonFunctions _PersonFunctions;
    private readonly IAddressFunctions _addressFunctions;

    public PersonController(
        IPersonFunctions PersonFunctions,
        IAddressFunctions addressFunctions)
    {
        _PersonFunctions = PersonFunctions;
        _addressFunctions = addressFunctions;
    }
}
```

### 14.2 Interface Dependencies
**RULE**: Depend on interfaces, not concrete implementations.

✅ **CORRECT:**
```csharp
private readonly IOrderRepository _repository;  // Interface

public PersonFunctions(IOrderRepository repository)
{
    _repository = repository;
}
```

❌ **INCORRECT:**
```csharp
private readonly OrderRepository _repository;  // Concrete class

public PersonFunctions(OrderRepository repository)
{
    _repository = repository;
}
```

### 14.3 Primary Constructors (C# 12+)
**RULE**: Use primary constructors for simple classes with injected dependencies.

✅ **CORRECT:**
```csharp
// Repositories inject ILogger<T> and pass (context, logger) to the single-type-parameter
// GenericCrud<TEntity> base, which owns exception logging (see §11.5 / §15.1).
public class OrderNoteRepository(OrderContext context, ILogger<OrderNoteRepository> logger)
    : GenericCrud<OrderNote>(context, logger), IOrderNoteRepository
{
    public async Task<List<ActivityNote>> GetActivityNotesByActivityIdAsync(int activityId)
    {
        return await context.ActivityNotes
            .AsNoTracking()
            .Where(n => n.ActivityId == activityId)
            .OrderByDescending(n => n.DateCreated)
            .ToListAsync();
    }
}
```

---

## 15. Cross-Cutting Concerns

These patterns are established across the current codebase (the production-readiness hardening). New code must follow them.

### 15.1 Error contract: RFC 7807 ProblemDetails
All error responses are ProblemDetails (`application/problem+json`) with `status`/`title`/`detail`/`traceId`. Shaping happens **once** in `GlobalExceptionHandler` (`IExceptionHandler`, registered via `AddProblemDetails()` + `AddExceptionHandler<GlobalExceptionHandler>()` + `UseExceptionHandler()`). Throw domain exceptions; never assemble error responses or `StatusCode(500, "...")` in actions. 500 detail is suppressed (no PII/PHI) and logged; domain messages (404/409/400) are echoed. `DuplicateOrderException` adds a `orderId` extension member. See §11.4–§11.5.

### 15.2 Optimistic concurrency
`BaseEntity.RowVersion` (`byte[]`) is the concurrency token. **Do not configure it per service** — call the shared `modelBuilder.ApplyFoundationConcurrencyTokens(Database)` from `Foundation.Persistence`, which picks the strategy for the active provider and is a no-op on non-relational providers so the InMemory test provider is unaffected:

- **SQL Server** — mapped to a `rowversion` column via `IsRowVersion()`.
- **PostgreSQL** — there is no `rowversion` type, so the *same* `RowVersion` property is mapped onto the always-present system `xmin` column (store type `xid`) through a fixed little-endian `byte[]`↔`uint` converter. Npgsql omits system columns from generated DDL, so `xmin` is never created as a column even though it appears in the migration's `CreateTable` call.

`GetById` surfaces the token as an `ETag` header; `PATCH` honors `If-Match` (copied onto the model before the update). `GenericCrud.UpdateEntityAsync` applies a client-supplied token as the tracked entry's `OriginalValue` so a concurrent change throws `DbUpdateConcurrencyException`, translated to `ConcurrencyConflictException` → 409. A malformed or truncated `If-Match` value must fail that check (409), never throw on the way to the database (500).

> **RowVersion must stay a real CLR property.** Mapping the token as an EF *shadow* property instead (e.g. `Ignore(RowVersion)` plus `Property<uint>("xmin")`) silently disables the whole contract: reads never populate `RowVersion`, so no `ETag` is emitted, and `GenericCrud` resolves a caller-supplied token by reflecting over `IProperty.PropertyInfo`, which is `null` for a shadow property — so `If-Match` is ignored and optimistic concurrency degrades to last-write-wins with no 409. Model-build tests alone will not catch this; see §15.12.

### 15.3 Partial updates and creation-audit protection
`GenericCrud.UpdateEntityAsync` loads the tracked original and copies incoming scalars via `CurrentValues.SetValues`, so the UPDATE touches only genuinely-changed columns. `OrderContext.SaveChangesAsync` stamps `UpdatedDate`, and on updates marks `CreatedDate`/`CreatedById` `IsModified = false` so the creation audit can never be overwritten.

### 15.4 Soft-delete global query filter
A relational-only global query filter (`HasQueryFilter(e => e.IsActive)`), applied to every `BaseEntity` type, excludes soft-deleted rows from LINQ queries by default. `FindAsync` key lookups bypass it (so update/soft-delete flows still load the row). Callers that need inactive rows (e.g. `SearchOrdersAsync` honoring an explicit `IncludeInactive` flag) opt out with `IgnoreQueryFilters()`. This filter and the concurrency-token call (§15.2) both live behind the same `if (!Database.IsRelational()) return;` guard in `OrderContext.Concurrency.cs` — the filter in a per-entity loop, the token as the single shared `ApplyFoundationConcurrencyTokens(Database)` call.

### 15.5 Caching (HybridCache)
Caching uses `HybridCache` (L1 in-memory + optional L2 Redis when `Caching:Redis:ConnectionString` is set), with a configurable lookup TTL (`Caching:LookupTtlMinutes`, default 30). **Every cache key for per-tenant data must include `tenantId`** so entries can't leak across tenants on the shared L2; only tenant-invariant data may use a shared key.

### 15.6 Lookups: reference tables surfaced as CustomTypes
Lookup/reference data (order types, statuses, priorities and similar) are seeded reference tables read through `ILookupRepository` / `LookupFunctions` (all `.AsNoTracking()`) and surfaced on the owning controller via `GET .../CustomTypes` — there is no per-lookup CRUD endpoint. They are cached via HybridCache (§15.5). Any enum-only lookup, where present, is projected from a DTO enum rather than queried.

### 15.7 Idempotency for POST
`IdempotencyMiddleware` is opt-in: when a POST carries an `Idempotency-Key` header, the first 2xx response (status/body/content-type/Location) is stored in `IDistributedCache` (in-memory locally, Redis in prod) keyed `idem:{tenantId}:{key}` (TTL `Idempotency:TtlHours`, default 24); a retry replays it (with `Idempotency-Replayed: true`) without re-running the endpoint. POSTs without the header are unchanged.

### 15.8 Request enrichment middleware
`CorrelationIdMiddleware` honors/generates `X-Correlation-ID`, pushes it onto Serilog `LogContext`, and echoes it. `TenantIdLogEnrichmentMiddleware` pushes the route `tenantId`. Both run after `UseRouting` and ahead of `UseSerilogRequestLogging` so every log line (including the request summary) carries them; `IdempotencyMiddleware` runs after request logging. Pipeline order is load-bearing.

### 15.9 Server-managed binary assets: private storage + streaming endpoint
Foundation has no binary-asset domain. **If a service adds one**, follow this pattern: a **private** blob container (`PublicAccessType.None`), served only through an authorized streaming endpoint (never a raw blob URL), with the persisted model URL rewritten to that stable API path.

### 15.10 Multi-tenancy & connection security
`TenantService` resolves `tenantId` from the route and builds a per-tenant connection string through an `ITenantConnectionStringBuilder` seam — `PostgresTenantConnectionStringBuilder` is the registered implementation, substituting the tenant database via `NpgsqlConnectionStringBuilder.Database` from the tenant connection-string template (never raw `string.Replace`) and rejecting non-positive-integer tenant ids (connection-string-injection defense). The `tenantId` route segment carries an `:int:min(1)` constraint. Per-tenant ADO.NET pool sizing is bounded (configurable via `Tenant:MaxPoolSize`/`MinPoolSize`).

### 15.11 Health checks
`GET /health/live` (process) and `GET /health/ready` probes via `AddHealthChecks()`. A service with no external dependency can use a `self` readiness check; per-tenant PostgreSQL databases are intentionally not probed (no tenant context outside a request).

### 15.12 Test provider selection
- Repository/CRUD tests use the **InMemory** provider with a unique `Guid.NewGuid()` database name; repositories are constructed with a `NullLogger` (`NullLogger<T>.Instance`).
- Relational-only behavior (concurrency token + soft-delete query filter) is exercised by building the model under the **Npgsql provider** (no connection opened); the `DbUpdateConcurrencyException` → `ConcurrencyConflictException` path is tested with a throwing `DbContext` subclass over the InMemory provider.
- **A model-build test is not sufficient for the concurrency contract.** Asserting the mapping exists does not prove EF reads the store-generated token back, which is what makes the `ETag` non-null and lets a stale `If-Match` produce a 409. Cover the token shape explicitly — that `RowVersion` is mapped, has a non-null `PropertyInfo` (not a shadow property), carries the expected column/store type, and round-trips through its converter — and put the end-to-end behavior in an **opt-in live-PostgreSQL suite** (`PostgresConcurrencyTests`, gated on a connection-string environment variable such as `TEST_POSTGRES`, reporting `Assert.Inconclusive` when unset so CI needs no database). That suite asserts a token is populated after insert and on read, a stale `If-Match` raises `ConcurrencyConflictException` without overwriting the winner, a malformed token yields the same 409 rather than a 500, and the token advances after a successful commit. Note that SQLite is relational but *not* Npgsql, so a SQLite-based concurrency test exercises only the `rowversion` branch and never the PostgreSQL one.
- Controllers are unit-tested by direct construction with mocked `I*Functions`; a `DefaultHttpContext` is assigned to `ControllerContext` when an action reads `Request`/`Response` headers (ETag / If-Match). MSTest exception assertions use `Assert.ThrowsExactlyAsync<T>`.

---

## Appendix A: Common Patterns Reference

### Phone Number Validation Pattern
```csharp
[RegularExpression(@"^(?:\+1\s?)?(?:\([2-9][0-9]{2}\)|[2-9][0-9]{2})[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}$",
    ErrorMessage = "Phone number must be a valid phone number")]
```

### AutoFixture Phone Number Generation
```csharp
// HomePhone: 555-1XX-XXXX (range 555-110 to 555-199)
.With(i => i.HomePhone, () => $"555-1{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")

// WorkPhone: 555-2XX-XXXX (range 555-210 to 555-299)
.With(i => i.WorkPhone, () => $"555-2{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")

// MobilePhone: 555-3XX-XXXX (range 555-310 to 555-399)
.With(i => i.MobilePhone, () => $"555-3{_fixture.Create<int>() % 90 + 10:00}-{_fixture.Create<int>() % 9000 + 1000:0000}")
```

### SSN Validation Pattern
```csharp
[RegularExpression(@"^\d{3}-\d{2}-\d{4}$", ErrorMessage = "SSN must be in the format ###-##-####")]
[StringLength(11, ErrorMessage = "SSN must be 11 characters including dashes")]
```

---

## Appendix B: Code Review Checklist

Use this checklist during code reviews:

### General Code Quality
- [ ] No use of `var` keyword - all types are explicit
- [ ] Indentation uses 4 spaces, not tabs
- [ ] Lines kept under 120 characters where practical
- [ ] All async methods end with `Async` suffix
- [ ] Null checks are performed where appropriate
- [ ] Comments explain "why" not "what"
- [ ] One class per file, file name matches class name

### Code Coverage Attributes
- [ ] All test classes have `[ExcludeFromCodeCoverage]` attribute
- [ ] All POCO classes (Entities, DTOs, Models) have `[ExcludeFromCodeCoverage]` attribute
- [ ] EF migration Designer.cs and ModelSnapshot.cs files have `[ExcludeFromCodeCoverage]` attribute
- [ ] Classes with business logic do NOT have `[ExcludeFromCodeCoverage]`
- [ ] Every non-test assembly reports ≥ 90% line coverage (enforced by Stop hook)

### Testing Standards
- [ ] Test methods follow `MethodName_ExpectedBehavior_WhenCondition` pattern
- [ ] AAA (Arrange-Act-Assert) pattern used in all tests
- [ ] Tests are independent and can run in any order
- [ ] AutoFixture configured for circular references in test setup
- [ ] Navigation properties omitted in AutoFixture customizations
- [ ] Phone numbers use distinct ranges (555-1XX, 555-2XX, 555-3XX)
- [ ] Test setup methods have comprehensive documentation

### Entity Framework and Data Access
- [ ] `AsNoTracking()` used for read-only queries
- [ ] `Include()` used for navigation properties that are accessed
- [ ] InMemory database uses unique GUID for each test

### Architecture and Dependencies
- [ ] Dependencies injected through constructor
- [ ] Interfaces used instead of concrete implementations
- [ ] Layered architecture maintained (API → BusinessLogic → DAL)

### Validation and Error Handling
- [ ] Custom exceptions used for domain-specific errors
- [ ] Data annotations used for validation
- [ ] Controller exception handling returns appropriate HTTP status codes
- [ ] All `catch` blocks log the exception via Serilog (`_logger.LogError`) except `ResourceNotFoundException` catches

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-05-06 | Initial document creation based on reference-service patterns |
| 1.1 | 2026-05-23 | Added rule 11.5: all catch blocks must use Serilog to log exceptions except ResourceNotFoundException |
| 1.2 | 2026-05-23 | Clarified §5.1 exception: only [TestMethod]-decorated methods are exempt from the Async suffix, not all methods in a [TestClass] file |
| 1.3 | 2026-06-13 | Reconciled to a hardened reference service: §11.4 controllers throw to a global ProblemDetails handler (no per-action try/catch; DuplicateOrderException→409 with caseId); §11.5 reframed around injected ILogger in GenericCrud/handler with business outcomes non-logged; §9.4 BaseEntity adds RowVersion; §14.3 GenericCrud example fixed to the single-type-parameter (context, logger) form; added §15 Cross-Cutting Concerns (ProblemDetails, optimistic concurrency, partial-update audit, soft-delete filter, HybridCache, lookups, idempotency, request-enrichment middleware, multi-tenancy/connection security, health checks, test-provider selection) |
| 1.4 | 2026-07-27 | Reconciled to the Azure SQL → PostgreSQL migration: §15.2 rewritten around the shared `ApplyFoundationConcurrencyTokens` seam (rowversion on SQL Server, the system `xmin` column via a `byte[]`↔`uint` converter on PostgreSQL) with an explicit warning that mapping the token as an EF shadow property silently disables ETag/If-Match; §9.4 BaseEntity comment updated to match; §15.4 corrected to describe the shared call rather than an inline provider branch; §15.10 per-tenant connection strings now built through the `ITenantConnectionStringBuilder` seam (`PostgresTenantConnectionStringBuilder`/`NpgsqlConnectionStringBuilder.Database`); §15.11 wording; §15.12 adds the rule that a model-build test alone cannot cover the concurrency contract, requiring token-shape assertions plus an opt-in live-PostgreSQL suite, and notes SQLite exercises only the rowversion branch |

---

## Document Approval

This document applies to the Foundation libraries and to services that consume them.

**Approved by**: [To be filled]
**Date**: [To be filled]
