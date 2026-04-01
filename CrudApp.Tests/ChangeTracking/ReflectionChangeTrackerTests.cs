using CrudApp.ChangeTracking;
using FluentAssertions;

namespace CrudApp.Tests.ChangeTracking;

public class ReflectionChangeTrackerTests
{
    private readonly ReflectionChangeTracker<TestEntity> _tracker = new();

    [Fact]
    public void DetectsPropertyChanges()
    {
        var old = new TestEntity { Name = "Alpha", Description = "Old desc", Price = 10m };
        var current = new TestEntity { Name = "Beta", Description = "New desc", Price = 20m };

        var changes = _tracker.DetectChanges(current, old);

        changes.Should().Contain(c => c.Key == "Name" && c.Value == "Alpha");
        changes.Should().Contain(c => c.Key == "Description" && c.Value == "Old desc");
        changes.Should().Contain(c => c.Key == "Price" && c.Value == "10");
    }

    [Fact]
    public void MasksSensitiveProperties()
    {
        var old = new TestEntity { Secret = "old-secret" };
        var current = new TestEntity { Secret = "new-secret" };

        var changes = _tracker.DetectChanges(current, old);

        changes.Should().Contain(c => c.Key == "Secret" && c.Value == "***");
    }

    [Fact]
    public void IgnoresIdAndRowVersion()
    {
        var old = new TestEntity { Id = "1", RowVersion = 1 };
        var current = new TestEntity { Id = "2", RowVersion = 5 };

        var changes = _tracker.DetectChanges(current, old);

        changes.Should().NotContain(c => c.Key == "Id");
        changes.Should().NotContain(c => c.Key == "RowVersion");
    }

    [Fact]
    public void IgnoresAuditAndSoftDeleteFields()
    {
        var old = new TestEntity
        {
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedBy = "user1",
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedBy = "user1",
            IsDeleted = false
        };
        var current = new TestEntity
        {
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "user2",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "user2",
            IsDeleted = true
        };

        var changes = _tracker.DetectChanges(current, old);

        changes.Should().NotContain(c => c.Key == "CreatedAt");
        changes.Should().NotContain(c => c.Key == "CreatedBy");
        changes.Should().NotContain(c => c.Key == "UpdatedAt");
        changes.Should().NotContain(c => c.Key == "UpdatedBy");
        changes.Should().NotContain(c => c.Key == "IsDeleted");
    }

    [Fact]
    public void ReturnsEmptyForIdenticalEntities()
    {
        var entity = new TestEntity { Name = "Same", Description = "Same", Price = 5m, Secret = "same" };

        var changes = _tracker.DetectChanges(entity, entity);

        changes.Should().BeEmpty();
    }
}
