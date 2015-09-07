﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using With.ConstructorProvider;
using With.Helpers;
using With.Naming;

namespace With
{
    /// <summary>
    /// Provides 'With' method on all classes
    /// </summary>
    public static class WithExtensions
    {
        /// <summary>
        /// Static constructor, used to instantiate default constructor provider.
        /// </summary>
        static WithExtensions()
        {
            // Default constructor, using pure reflection
            ////ConstructorProvider = ctor => ctor.Invoke;

            // For better performances, we put in cache compiled constructors
            ConstructorProvider = CacheConstructorProvider.New(
                                    ExpressionConstructorProvider.CreateConstructor);
        }

        /// <summary>
        /// Constructor provider used by the extension
        /// </summary>
        public static Func<ConstructorInfo, Constructor> ConstructorProvider
        {
            get;
            set;
        }

        /// <summary>
        /// Creates a query to copy and update an object.
        /// </summary>
        /// <typeparam name="TSource">Type of the object to 'copy and update'</typeparam>
        /// <typeparam name="TMember">Type of the field/property to update</typeparam>
        /// <param name="source">Object to copy and update</param>
        /// <param name="memberSelector">Selector on the field/property to update</param>
        /// <param name="memberValue">New value for the field/property</param>
        /// <returns>Query used to create the new desired object</returns>
        public static CopyUpdateQuery<TSource> With<TSource, TMember>(
            this TSource source, 
            Expression<Func<TSource, TMember>> memberSelector, 
            TMember memberValue)
            where TSource : class
        {
            // Get field/property name accessed by the selector
            var memberName = GetReturnedMemberName(memberSelector);
            var emptyList = Enumerable.Empty<KeyValuePair<string, object>>();

            // Create query
            return new CopyUpdateQuery<TSource>(
                source,
                emptyList.Concat(KeyValuePair.Create(memberName, (object)memberValue)));
        }

        /// <summary>
        /// Creates a query to copy and update an object.
        /// </summary>
        /// <typeparam name="TSource">Type of the object to 'copy and update'</typeparam>
        /// <typeparam name="TMember">Type of the field/property to update</typeparam>
        /// <param name="query">Current query to update</param>
        /// <param name="memberSelector">Selector on the field/property to update</param>
        /// <param name="memberValue">New value for the field/property</param>
        /// <returns>Query used to create the new desired object</returns>
        public static CopyUpdateQuery<TSource> With<TSource, TMember>(
            this CopyUpdateQuery<TSource> query, 
            Expression<Func<TSource, TMember>> memberSelector, 
            TMember memberValue)
            where TSource : class
        {
            // Get field/property name accessed by the selector
            var memberName = GetReturnedMemberName(memberSelector);

            // Create query
            return new CopyUpdateQuery<TSource>(
                query.Source,
                query.MemberValues.Concat(KeyValuePair.Create(memberName, (object)memberValue)));
        }

        /// <summary>
        /// Execute a query to copy and update an object.
        /// </summary>
        /// <typeparam name="TSource">Type of the object to 'copy and update'</typeparam>
        /// <param name="query">Query to execute</param>
        /// <param name="getMemberNameFromArgument">
        /// Returns the member name corresponding to a given argument name.
        /// If not specified, pascal case convention is used.
        /// Only useful if you use a different naming convention for your members ('m_' prefix for example)
        /// </param>
        /// <returns>New object, with updated values</returns>
        public static TSource Create<TSource>(this CopyUpdateQuery<TSource> query, Func<string, string> getMemberNameFromArgument = null)
            where TSource : class
        {
            getMemberNameFromArgument = getMemberNameFromArgument ?? PascalCase.Convert;

            var typeToBuild = typeof(TSource);

            // Check if unique constructor is available
            var typeInfo = typeToBuild.GetTypeInfo();
            var ctorInfos = typeInfo.DeclaredConstructors;
            if (1 != ctorInfos.Count())
                throw new InvalidOperationException("Type " + typeToBuild + " must only contain one constructor");

            // Get constructor parameters
            var ctorInfo = ctorInfos.First();
            var ctorParams = ctorInfo.GetParameters();

            // Get arguments values
            var arguments = ctorParams.Select((arg, index) =>
            {
                // TODO : can be optimized
                var memberName = getMemberNameFromArgument(arg.Name);
                var newValue = query.MemberValues.Where(keyValue => keyValue.Key == memberName).Select(keyValue => keyValue.Value).FirstOrDefault();
                if (null != newValue)
                    return newValue;

                // Field ?
                var fieldInfo = typeToBuild.GetRuntimeField(memberName);
                if (null != fieldInfo)
                    return fieldInfo.GetValue(query.Source);

                // Property ?
                var propertyInfo = typeToBuild.GetRuntimeProperty(memberName);
                if (null != propertyInfo)
                    return propertyInfo.GetValue(query.Source);

                throw new InvalidOperationException(
                    string.Format(
                        "Unable to find a value matching constructor argument named '{0}'",
                        arg.Name));
            }).ToArray();

            var constructor = ConstructorProvider(ctorInfo);
            return (TSource)constructor(arguments);
        }

        /// <summary>
        /// Retrieve member name returned by a lambda expression.
        /// </summary>
        /// <typeparam name="TSource">Type of the object owning the member</typeparam>
        /// <typeparam name="TMember">Type of the member</typeparam>
        /// <param name="selector">Lambda expression to inspect</param>
        /// <returns>Member name returned by the lambda expression (lowered)</returns>
        private static string GetReturnedMemberName<TSource, TMember>(Expression<Func<TSource, TMember>> selector)
            where TSource : class
        {
            // Check if lambda is valid
            var memberExpression = selector.Body as MemberExpression;
            if (null == memberExpression)
                throw new ArgumentException(
                    string.Format(
                        "Lambda '{0}'is not a member access",
                        selector.Name));

            // Check if lambda is a field/property access
            var isFieldOrPropertyAccess = memberExpression.Member is FieldInfo || memberExpression.Member is PropertyInfo;
            if (!isFieldOrPropertyAccess)
                throw new ArgumentException(
                    string.Format(
                        "Lambda '{0}' is not a field/property access",
                        selector.Name));

            // Check if field/property is accessed from lambda parameter
            if (selector.Parameters[0] != memberExpression.Expression)
                throw new ArgumentException(
                    string.Format(
                        "Field/property not accessed from source '{0}'",
                        selector.Parameters[0].Name));

            return memberExpression.Member.Name;
        }
    }
}
