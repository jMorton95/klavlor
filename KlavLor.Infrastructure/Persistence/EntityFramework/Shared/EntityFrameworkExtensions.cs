using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using KlavLor.Application.Common;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Shared;

public static class EntityFrameworkExtensions
{
    public static IQueryable<T> WithRelationships<T>(
        this DbSet<T> dbSet, Func<DbSet<T>, IIncludableQueryable<T, object>> navigation) where T : Entity
    {
        return navigation.Invoke(dbSet);
    }

    public static IQueryable<TResponse> ProjectToDto<TEntity, TResponse>(this IQueryable<TEntity> set,
        Expression<Func<TEntity, TResponse>> projection) where TEntity : Entity
    {
        return set.Select(projection);
    }

    public static IQueryable<TEntity> WithPaging<TEntity>(this IQueryable<TEntity> set,
        PagedQuery pagedQuery) where TEntity : Entity
    {
        return set.Skip((pagedQuery.PageNumber - 1) * pagedQuery.PageSize).Take(pagedQuery.PageSize);
    }

    public static IQueryable<T> SortByProperty<T>(
        this IQueryable<T> query,
        string? sortBy,
        SortDirection sortDirection)
        where T : Entity
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return query.OrderBy(x => x.Id);

        var param = Expression.Parameter(typeof(T), "x");
        var property = Expression.PropertyOrField(param, sortBy);

        var lambda = Expression.Lambda(property, param);

        var methodName = sortDirection == SortDirection.Ascending
            ? nameof(Queryable.OrderBy)
            : nameof(Queryable.OrderByDescending);

        var method = typeof(Queryable)
            .GetMethods()
            .Single(m =>
                m.Name == methodName &&
                m.GetParameters().Length == 2);

        var genericMethod = method.MakeGenericMethod(
            typeof(T),
            property.Type);

        return (IQueryable<T>)genericMethod.Invoke(
            null, [query, lambda])!;
    }
}
