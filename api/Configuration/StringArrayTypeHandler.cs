using System.Data;
using Dapper;

namespace VinLoggen.Api.Configuration;

/// <summary>
/// Dapper type handler for PostgreSQL TEXT[] columns ↔ C# string[].
/// </summary>
public sealed class StringArrayTypeHandler : SqlMapper.TypeHandler<string[]>
{
    public override void SetValue(IDbDataParameter parameter, string[]? value)
    {
        parameter.Value = value ?? (object)DBNull.Value;
    }

    public override string[] Parse(object value) => value switch
    {
        string[] arr => arr,
        _ => []
    };
}
