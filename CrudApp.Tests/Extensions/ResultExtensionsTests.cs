using CrudApp.Enums;
using CrudApp.Extensions;
using CrudApp.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CrudApp.Tests.Extensions;

public class ResultExtensionsTests
{
    [Fact]
    public void Success_Returns200()
    {
        var result = new Result<string>()
            .SetReturnType(ReturnType.Success)
            .SetData("test");

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<Ok<Result<string>>>();
    }

    [Fact]
    public void NotFound_Returns404()
    {
        var result = new Result<string?>()
            .SetReturnType(ReturnType.NotFound);

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<NotFound<Result<string?>>>();
    }

    [Fact]
    public void EntityIsNull_Returns404()
    {
        var result = new Result<string?>()
            .SetReturnType(ReturnType.EntityIsNull);

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<NotFound<Result<string?>>>();
    }

    [Fact]
    public void InvalidVersion_Returns409()
    {
        var result = new Result<string>()
            .SetReturnType(ReturnType.InvalidVersion);

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<Conflict<Result<string>>>();
    }

    [Fact]
    public void InvalidOperation_Returns400()
    {
        var result = new Result<string>()
            .SetReturnType(ReturnType.InvalidOperation);

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<BadRequest<Result<string>>>();
    }

    [Fact]
    public void CollectionIsEmpty_Returns200()
    {
        var result = new Result<IList<string>>()
            .SetReturnType(ReturnType.CollectionIsEmpty)
            .SetData(new List<string>());

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<Ok<Result<IList<string>>>>();
    }

    [Fact]
    public void TooManyRecords_Returns400()
    {
        var result = new Result<IList<string>>()
            .SetReturnType(ReturnType.TooManyRecords);

        var httpResult = result.ToHttpResult();

        httpResult.Should().BeOfType<BadRequest<Result<IList<string>>>>();
    }
}
