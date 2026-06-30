using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Parse.Infrastructure.Utilities;



public static class ExpressionHelper
{
    /// <summary>
    /// Recursively extracts the Parse field name path from a lambda expression.
    /// Handles deep nested paths like: x => x.Parent.Child.Name -> "parent.child.name"
    /// and correctly respects [ParseFieldName] attributes at every level.
    /// </summary>
    public static string GetParseFieldName<T, TProp>(Expression<Func<T, TProp>> propertyExpression)
    {
        return GetPath(propertyExpression.Body);
    }

    private static string GetPath(Expression expression)
    {
        // 1. Unwrap value types (int, bool, enums) wrapped in Unary/Convert expressions
        if (expression is UnaryExpression unaryExpr)
        {
            return GetPath(unaryExpr.Operand);
        }

        // 2. Process member access (properties and fields)
        if (expression is MemberExpression memberExpr)
        {
            string memberName = GetFieldName(memberExpr.Member);

            // If the parent is also a member access or convert, recurse deeper
            if (memberExpr.Expression is MemberExpression || memberExpr.Expression is UnaryExpression)
            {
                string parentPath = GetPath(memberExpr.Expression);
                return $"{parentPath}.{memberName}";
            }

            // Base case: parent is the parameter (e.g., 'x' in 'x.Name')
            if (memberExpr.Expression is ParameterExpression)
            {
                return memberName;
            }

            return memberName;
        }

        throw new ArgumentException("Expression must be a valid property access path (e.g., x => x.Parent.Child.Name).");
    }

    private static string GetFieldName(MemberInfo memberInfo)
    {
        var attribute = memberInfo.GetCustomAttribute<ParseFieldNameAttribute>();
        return attribute != null ? attribute.FieldName : memberInfo.Name;
    }
}