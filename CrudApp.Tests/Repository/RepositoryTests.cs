using CrudApp.Entity;
using CrudApp.Enums;
using CrudApp.Repository;
using CrudApp.Models;
using FluentAssertions;
using Moq;

namespace CrudApp.Tests.Repository;

public class RepositoryTests
{
    private readonly Mock<IRepository<TestEntity>> _repositoryMock;

    public RepositoryTests()
    {
        _repositoryMock = new Mock<IRepository<TestEntity>>();
    }

    [Fact]
    public async Task AddAsync_SetsAuditFields()
    {
        var entity = new TestEntity { Name = "Test", Description = "Desc" };
        var expectedResult = new Result<TestEntity>()
            .SetReturnType(ReturnType.Success)
            .SetData(new TestEntity
            {
                Id = "generated-id",
                Name = "Test",
                Description = "Desc",
                RowVersion = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "system",
                UpdatedBy = "system",
                IsDeleted = false
            });

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<TestEntity>(), default))
            .ReturnsAsync(expectedResult);

        var result = await _repositoryMock.Object.AddAsync(entity);

        result.Type.Should().Be(ReturnType.Success);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be("generated-id");
        result.Data.RowVersion.Should().Be(1);
        result.Data.CreatedBy.Should().Be("system");
        result.Data.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_RejectsEntityWithId()
    {
        _repositoryMock.Setup(r => r.AddAsync(It.Is<TestEntity>(e => !string.IsNullOrEmpty(e.Id)), default))
            .ReturnsAsync(new Result<TestEntity>()
                .SetReturnType(ReturnType.InvalidOperation)
                .SetDescription("Invalid request, this method accepts only insert."));

        var entity = new TestEntity { Id = "existing-id", Name = "Test" };
        var result = await _repositoryMock.Object.AddAsync(entity);

        result.Type.Should().Be(ReturnType.InvalidOperation);
    }

    [Fact]
    public async Task UpdateAsync_IncrementsRowVersion()
    {
        var updated = new TestEntity { Id = "1", Name = "New", RowVersion = 4 };
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestEntity>(), default))
            .ReturnsAsync(new Result<TestEntity>()
                .SetReturnType(ReturnType.Success)
                .SetData(updated));

        var result = await _repositoryMock.Object.UpdateAsync(updated);

        result.Type.Should().Be(ReturnType.Success);
        result.Data!.RowVersion.Should().Be(4);
    }

    [Fact]
    public async Task UpdateAsync_RejectsVersionMismatch()
    {
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestEntity>(), default))
            .ReturnsAsync(new Result<TestEntity>()
                .SetReturnType(ReturnType.InvalidVersion)
                .SetDescription("Entity version does not match."));

        var result = await _repositoryMock.Object.UpdateAsync(new TestEntity { Id = "1", RowVersion = 3 });

        result.Type.Should().Be(ReturnType.InvalidVersion);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDeletedEntity()
    {
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestEntity>(), default))
            .ReturnsAsync(new Result<TestEntity>()
                .SetReturnType(ReturnType.InvalidOperation)
                .SetDescription("Cannot update a deleted entity."));

        var result = await _repositoryMock.Object.UpdateAsync(new TestEntity { Id = "1", IsDeleted = true });

        result.Type.Should().Be(ReturnType.InvalidOperation);
        result.Description.Should().Contain("deleted");
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesEntity()
    {
        var softDeleted = new TestEntity { Id = "1", IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = "system", RowVersion = 2 };
        _repositoryMock.Setup(r => r.DeleteAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.Success)
                .SetData(softDeleted));

        var result = await _repositoryMock.Object.DeleteAsync("1");

        result.Type.Should().Be(ReturnType.Success);
        result.Data!.IsDeleted.Should().BeTrue();
        result.Data.DeletedAt.Should().NotBeNull();
        result.Data.DeletedBy.Should().Be("system");
        result.Data.RowVersion.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_RejectsAlreadyDeletedEntity()
    {
        _repositoryMock.Setup(r => r.DeleteAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.InvalidOperation)
                .SetDescription("Entity is already deleted."));

        var result = await _repositoryMock.Object.DeleteAsync("1");

        result.Type.Should().Be(ReturnType.InvalidOperation);
    }

    [Fact]
    public async Task PurgeAsync_PermanentlyRemovesEntity()
    {
        var entity = new TestEntity { Id = "1", Name = "Test" };
        _repositoryMock.Setup(r => r.PurgeAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.Success)
                .SetData(entity)
                .SetDescription("Entity permanently deleted."));

        var result = await _repositoryMock.Object.PurgeAsync("1");

        result.Type.Should().Be(ReturnType.Success);
        _repositoryMock.Verify(r => r.PurgeAsync("1", default), Times.Once);
    }

    [Fact]
    public async Task PurgeAsync_ReturnsNotFoundForMissingEntity()
    {
        _repositoryMock.Setup(r => r.PurgeAsync("missing", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.NotFound));

        var result = await _repositoryMock.Object.PurgeAsync("missing");

        result.Type.Should().Be(ReturnType.NotFound);
    }

    [Fact]
    public async Task RestoreAsync_UnmarksDeletedEntity()
    {
        var restored = new TestEntity { Id = "1", IsDeleted = false, DeletedAt = null, DeletedBy = null, RowVersion = 3 };
        _repositoryMock.Setup(r => r.RestoreAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.Success)
                .SetData(restored));

        var result = await _repositoryMock.Object.RestoreAsync("1");

        result.Type.Should().Be(ReturnType.Success);
        result.Data!.IsDeleted.Should().BeFalse();
        result.Data.DeletedAt.Should().BeNull();
        result.Data.DeletedBy.Should().BeNull();
        result.Data.RowVersion.Should().Be(3);
    }

    [Fact]
    public async Task RestoreAsync_RejectsNonDeletedEntity()
    {
        _repositoryMock.Setup(r => r.RestoreAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.InvalidOperation));

        var result = await _repositoryMock.Object.RestoreAsync("1");

        result.Type.Should().Be(ReturnType.InvalidOperation);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNullForDeletedEntity()
    {
        _repositoryMock.Setup(r => r.FindByIdAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.EntityIsNull)
                .SetData(default(TestEntity)));

        var result = await _repositoryMock.Object.FindByIdAsync("1");

        result.Type.Should().Be(ReturnType.EntityIsNull);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsActiveEntity()
    {
        var entity = new TestEntity { Id = "1", Name = "Active", IsDeleted = false };
        _repositoryMock.Setup(r => r.FindByIdAsync("1", default))
            .ReturnsAsync(new Result<TestEntity?>()
                .SetReturnType(ReturnType.Success)
                .SetData(entity));

        var result = await _repositoryMock.Object.FindByIdAsync("1");

        result.Type.Should().Be(ReturnType.Success);
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Active");
    }
}

public class BaseEntityBehaviorTests
{
    [Fact]
    public void BaseEntity_DefaultValues()
    {
        var entity = new TestEntity();

        entity.IsDeleted.Should().BeFalse();
        entity.RowVersion.Should().Be(0);
        entity.CreatedBy.Should().BeEmpty();
        entity.UpdatedBy.Should().BeEmpty();
        entity.DeletedAt.Should().BeNull();
        entity.DeletedBy.Should().BeNull();
    }

    [Fact]
    public void BaseEntity_ImplementsAllInterfaces()
    {
        var entity = new TestEntity();

        entity.Should().BeAssignableTo<IEntityId>();
        entity.Should().BeAssignableTo<IEntity<TestEntity>>();
        entity.Should().BeAssignableTo<IAuditableEntity>();
        entity.Should().BeAssignableTo<ISoftDeletable>();
        entity.Should().BeAssignableTo<IVersionable>();
    }

    [Fact]
    public void SoftDeletable_SetFields()
    {
        var entity = new TestEntity();
        var deletedAt = DateTime.UtcNow;

        entity.IsDeleted = true;
        entity.DeletedAt = deletedAt;
        entity.DeletedBy = "admin";

        entity.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().Be(deletedAt);
        entity.DeletedBy.Should().Be("admin");
    }

    [Fact]
    public void Auditable_SetFields()
    {
        var entity = new TestEntity();
        var now = DateTime.UtcNow;

        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        entity.CreatedBy = "user1";
        entity.UpdatedBy = "user2";

        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
        entity.CreatedBy.Should().Be("user1");
        entity.UpdatedBy.Should().Be("user2");
    }
}
