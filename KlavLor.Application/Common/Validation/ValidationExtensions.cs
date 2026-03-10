namespace KlavLor.Application.Common.Validation;

public static class ValidationExtensions
{
    public static bool IsValidPropertyName<T>(string sortBy)
    {
        return typeof(T)
            .GetProperties()
            .Any(p => string.Equals(p.Name, sortBy, StringComparison.OrdinalIgnoreCase));
    }
}
