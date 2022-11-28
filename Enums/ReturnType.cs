namespace CrudApp.Enums;

public enum ReturnType
{
    Unknown = -1,
    Success = 1,
    InvalidRequest = 2,
    EntityIsNull = 3,
    EntityIsChanged = 4,
    InvalidOperation = 5,
    TooManyRecords = 6,
    CollectionIsEmpty = 7,
    InvalidVersion = 8,
    NotFound = 9,
}