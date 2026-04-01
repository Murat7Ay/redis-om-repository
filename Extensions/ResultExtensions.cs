using CrudApp.Enums;
using CrudApp.Models;

namespace CrudApp.Extensions;

public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.Type switch
        {
            ReturnType.Success => Results.Ok(result),
            ReturnType.CollectionIsEmpty => Results.Ok(result),
            ReturnType.NotFound => Results.NotFound(result),
            ReturnType.EntityIsNull => Results.NotFound(result),
            ReturnType.InvalidOperation => Results.BadRequest(result),
            ReturnType.InvalidRequest => Results.BadRequest(result),
            ReturnType.InvalidVersion => Results.Conflict(result),
            ReturnType.TooManyRecords => Results.BadRequest(result),
            ReturnType.EntityIsChanged => Results.Conflict(result),
            _ => Results.StatusCode(500)
        };
    }
}
